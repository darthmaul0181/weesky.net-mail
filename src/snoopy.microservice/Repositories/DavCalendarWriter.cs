using System.Text;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Repositories;

/// <inheritdoc cref="IDavCalendarWriter"/>
internal sealed class DavCalendarWriter(
    CalendarEventStore store, ICalendarSyncStore sync, PreferencesDbContext context,
    ILogger<DavCalendarWriter> logger) : IDavCalendarWriter
{
    public async Task<DavWriteOutcome> PutAsync(Guid userId, Guid calendarId, string davName,
        string ics, CancellationToken cancellationToken, bool createOnly = false,
        string? ifMatch = null)
    {
        if (IcsGuards.CheckAll(ics, out var parsed) is { } refused) return Refused(refused);

        try
        {
            return await GateAsync(userId, calendarId, davName, ics, parsed!, createOnly, ifMatch,
                cancellationToken);
        }
        // Before the DbUpdateException arm, which it would otherwise be swallowed by: EF wraps the
        // provider's 1205 inside one, and replaying a lock wait would only wait again.
        catch (Exception e) when (DavWriteAnswer.IsTransient(e))
        {
            return Busy(e, "PUT", davName, userId);
        }
        catch (DbUpdateException first)
        {
            // The race of two creating PUTs: the loser passes the existence pre-check and dies on a
            // unique index. Replayed once — which is what the same PUT arrived a second later would
            // have been: a replacement of the winner's row, or, when the winner landed the UID
            // under ANOTHER name, the conflict the replay's own holder check names.
            logger.LogWarning(first,
                "PUT {DavName} for {UserId} hit a unique index; translating instead of failing",
                davName, userId);
            context.ChangeTracker.Clear();

            try
            {
                return await GateAsync(userId, calendarId, davName, ics, parsed!, createOnly,
                    ifMatch, cancellationToken);
            }
            catch (DbUpdateException second)
            {
                logger.LogError(second,
                    "PUT {DavName} for {UserId} failed twice; answering busy", davName, userId);
                context.ChangeTracker.Clear();
                return Refused(DavWriteStatus.Busy);
            }
        }
    }

    public async Task<DavWriteOutcome> DeleteAsync(Guid userId, Guid calendarId, string davName,
        CancellationToken cancellationToken, string? ifMatch = null)
    {
        var row = await FindAsync(userId, calendarId, davName, cancellationToken);
        if (row is null) return Refused(DavWriteStatus.NotFound);

        // The cheap net at this gate's own read; the decisive comparison is under the lock below.
        if (ifMatch is not null && !Holds(ifMatch, row))
            return Refused(DavWriteStatus.PreconditionFailed);

        try
        {
            return await store.InTransactionAsync(async () =>
            {
                var rank = await sync.NextSequenceAsync(calendarId, cancellationToken);

                // Re-read under the state lock: the archive keeps what is stored NOW, and the
                // decisive If-Match compares against it — a replacement committed since the read
                // above is the very version the header protects. A refusal rolls the rank back.
                if (!await ReloadAsync(row, cancellationToken)) return Refused(DavWriteStatus.NotFound);
                if (ifMatch is not null && !Holds(ifMatch, row))
                    return Refused(DavWriteStatus.PreconditionFailed);

                // EventId NULL: a delete revision outlives the row it describes.
                await sync.ArchiveAsync(userId, calendarId, null, row.Uid, row.DavName, row.IcsRaw,
                    RevisionCause.Delete, cancellationToken);

                // By hand, as every deletion of these stores does: the InMemory provider enforces
                // no foreign key, and this is what makes it behave like the cascade MariaDB runs.
                await store.ClearAttendeesAsync(row.Id, cancellationToken);
                context.CalendarEvents.Remove(row);
                await context.SaveChangesAsync(cancellationToken);

                await sync.PlaceTombstoneAsync(calendarId, davName, rank, cancellationToken);
                return new DavWriteOutcome(DavWriteStatus.Deleted, null, null, rank);
            }, outcome => outcome.Status is DavWriteStatus.Deleted, cancellationToken);
        }
        catch (Exception e) when (DavWriteAnswer.IsTransient(e))
        {
            return Busy(e, "DELETE", davName, userId);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The row vanished between the read and the write: this delete arrived second, and to
            // its sender that is the same 404 an absent name answers.
            context.ChangeTracker.Clear();
            return Refused(DavWriteStatus.NotFound);
        }
    }

    public async Task<DavWriteOutcome> DeleteAllAsync(
        Guid userId, Guid calendarId, CancellationToken cancellationToken)
    {
        try
        {
            // The collection itself first: an id this user does not hold reads no resource, and
            // without this it would answer Emptied — a 204 over a calendar that is not theirs.
            if (!await context.Calendars.AnyAsync(
                    c => c.Id == calendarId && c.UserId == userId, cancellationToken))
            {
                return Refused(DavWriteStatus.NotFound);
            }

            // The ids first, so an empty calendar opens no transaction and spends no rank; each
            // batch re-reads its rows under its own lock. The read sits inside the try too: a
            // transient failure here is the lock race the catch answers Busy for, not a 500.
            var ids = await context.CalendarEvents
                .Where(e => e.CalendarId == calendarId && e.UserId == userId)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0) return Emptied;

            var buried = 0;
            foreach (var chunk in ids.Chunk(CalendarStore.DeleteBatch))
            {
                // A List, not the chunk array: EF's InMemory translator cannot funclet an array's
                // span-based Contains, which C#'s extension resolution now prefers.
                var batch = chunk.ToList();
                // Tombstoned, unlike the sidebar's deletion: the collection survives, so every
                // other device learns of each resource by name rather than losing the lot.
                buried += await store.InTransactionAsync(
                    () => CalendarBatchDelete.RunAsync(context, sync, userId, calendarId, batch,
                        tombstones: true, cancellationToken),
                    cancellationToken);
            }

            logger.LogInformation("DELETE of calendar {CalendarId} for {UserId} buried {Count} events",
                calendarId, userId, buried);
            return Emptied;
        }
        catch (Exception e) when (DavWriteAnswer.IsTransient(e))
        {
            return Busy(e, "DELETE", "the calendar", userId);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The rows vanished under the batch — someone emptied the calendar first, and to this
            // sender that is the same 204 an already-empty calendar answers.
            context.ChangeTracker.Clear();
            return Emptied;
        }
    }

    public async Task<bool> ArchiveRejectedAsync(Guid userId, Guid calendarId, string davName,
        string ics, CancellationToken cancellationToken)
    {
        // A revision may not outweigh what a stored file may: the ceiling is translated here
        // rather than surfacing as a database refusal on the 412 path.
        if (Encoding.UTF8.GetByteCount(ics) > IcsGuards.MaxIcsBytes) return false;

        // It is an archive, not a resource: a body that parses into nothing is kept with no UID.
        var uid = IcsDocument.TryLoad(ics) is { } parsed ? UidOf(parsed) : null;

        try
        {
            await sync.ArchiveAsync(userId, calendarId, null, uid, davName, ics,
                RevisionCause.Rejected, cancellationToken);
            return true;
        }
        catch (Exception e) when (DavWriteAnswer.IsTransient(e))
        {
            // The archive is a courtesy beside a refusal already decided, and this insert is the
            // one write on the 412 path: a lock wait here would turn a correct 412 into the 500 a
            // client retries on the same resource for ever — and its precondition would fail again.
            logger.LogWarning(e,
                "Archiving the refused body for {DavName} of {UserId} lost a lock race",
                davName, userId);
            context.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task<DavWriteOutcome> GateAsync(Guid userId, Guid calendarId, string davName,
        string ics, IcsCalendar parsed, bool createOnly, string? ifMatch,
        CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, calendarId, davName, cancellationToken);

        // If-Match, first — RFC 7232 § 6 orders it before If-None-Match — at this gate's own read:
        // the cheap net over a row that vanished or moved since the edge's pre-check. The decisive
        // comparison runs again below, under the state lock.
        if (ifMatch is not null && !Holds(ifMatch, row))
            return Refused(DavWriteStatus.PreconditionFailed);

        // Create-only, and the name already holds a resource: the creation race's loser. Refused
        // before anything else — no rank, no archive, above all no replacement of the winner.
        if (createOnly && row is not null) return Refused(DavWriteStatus.AlreadyExists);

        // Byte-identical with what is already stored: nothing changes, so no transaction, no rank,
        // no client woken — the shape every idempotent DAVx5 retry takes.
        if (row is not null && string.Equals(row.IcsRaw, ics, StringComparison.Ordinal))
            return new DavWriteOutcome(DavWriteStatus.Replaced, EntityTag(row), null, row.SyncSequence);

        var uid = UidOf(parsed);

        return await store.InTransactionAsync(async () =>
        {
            // The state row's lock FIRST, always, and before any row is touched — the order every
            // door of these two stores takes, or they deadlock against each other.
            var rank = await sync.NextSequenceAsync(calendarId, cancellationToken);

            // The collection itself, under the lock: it is what ApplyIcsAsync projects in, and one
            // deleted between the controller's resolution and here leaves this door.
            var calendar = await context.Calendars.SingleOrDefaultAsync(
                c => c.Id == calendarId && c.UserId == userId, cancellationToken);
            if (calendar is null) return Refused(DavWriteStatus.NotFound);

            // Re-read under the lock: everything judged above was judged on a row a concurrent
            // writer may have replaced or removed since, and the archive below must keep what is
            // stored NOW — or that version never enters calendar_revisions.
            if (row is not null && !await ReloadAsync(row, cancellationToken)) row = null;

            // The decisive If-Match comparison — the only one a conditional write may trust. The
            // rank rolls back with the refusal (the commit predicate below), so no client is woken.
            if (ifMatch is not null && !Holds(ifMatch, row))
                return Refused(DavWriteStatus.PreconditionFailed);
            if (createOnly && row is not null) return Refused(DavWriteStatus.AlreadyExists);

            // RFC 4791 § 5.3.2.1: the UID must not be one ANOTHER resource of this calendar holds,
            // and the href names that holder — never the request URI. The same UID in another
            // calendar is no conflict.
            var holder = await context.CalendarEvents.AsNoTracking().FirstOrDefaultAsync(
                e => e.CalendarId == calendarId && e.Uid == uid && e.DavName != davName,
                cancellationToken);
            if (holder is not null)
                return Conflict(userId, calendar.DavName, holder.DavName);

            // The other half of no-uid-conflict, which § 5.3.2.1 spells in the same sentence:
            // « or overwrite an existing calendar object resource with one that has a different
            // UID ». The href names this very resource — the client must re-read what it holds.
            if (row is not null && !string.Equals(row.Uid, uid, StringComparison.Ordinal))
                return Conflict(userId, calendar.DavName, davName);

            if (row is null && await context.CalendarEvents.CountAsync(
                    e => e.CalendarId == calendarId, cancellationToken)
                >= CalendarEventStore.MaxPerCalendar)
                return Refused(DavWriteStatus.CollectionFull);

            var replacing = row is not null;
            if (row is null)
            {
                row = new CalendarEvent
                {
                    Id = Guid.NewGuid(), CalendarId = calendarId, UserId = userId, DavName = davName
                };
                context.CalendarEvents.Add(row);
            }
            else
            {
                // Archive before overwriting, in the same transaction — so under the same rank,
                // and never without it.
                await sync.ArchiveAsync(userId, calendarId, row.Id, row.Uid, row.DavName,
                    row.IcsRaw, RevisionCause.Put, cancellationToken);
            }

            await store.ApplyIcsAsync(row, calendar, ics, parsed, rank, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            // A tombstone and a living resource must never coexist on one name: a sync-collection
            // would return both, and the order the client applies them in would decide the fate.
            await sync.LiftTombstoneAsync(calendarId, davName, cancellationToken);

            return new DavWriteOutcome(
                replacing ? DavWriteStatus.Replaced : DavWriteStatus.Created, EntityTag(row), null, rank);
        }, outcome => outcome.Status is DavWriteStatus.Created or DavWriteStatus.Replaced,
            cancellationToken);
    }

    private Task<CalendarEvent?> FindAsync(
        Guid userId, Guid calendarId, string davName, CancellationToken cancellationToken) =>
        context.CalendarEvents.SingleOrDefaultAsync(
            e => e.CalendarId == calendarId && e.UserId == userId && e.DavName == davName,
            cancellationToken);

    /// <summary>Reloads a row read before the state lock; false when it no longer exists.</summary>
    private async Task<bool> ReloadAsync(CalendarEvent row, CancellationToken cancellationToken)
    {
        var entry = context.Entry(row);
        await entry.ReloadAsync(cancellationToken);
        return entry.State is not EntityState.Detached;
    }

    private DavWriteOutcome Busy(Exception e, string verb, string davName, Guid userId)
    {
        logger.LogWarning(e, "{Verb} {DavName} for {UserId} lost a lock race; answering busy",
            verb, davName, userId);
        context.ChangeTracker.Clear();
        return Refused(DavWriteStatus.Busy);
    }

    /// <summary>The identity the whole resource syncs on: the master's, or the first override's
    /// when the file holds overrides alone — every component shares it, the gate saw to that.</summary>
    private static string UidOf(IcsCalendar parsed) =>
        (IcsDocument.MasterOf(parsed) ?? IcsDocument.Components(parsed).First()).Uid ?? string.Empty;

    /// <summary>True when the row is a current representation the If-Match header covers — the
    /// STRONG comparison, shared with the edge through <see cref="EntityTagMatcher"/>.</summary>
    private static bool Holds(string ifMatch, CalendarEvent? row) =>
        row is not null && EntityTagMatcher.Match(ifMatch, EntityTag(row));

    private static string EntityTag(CalendarEvent row) => DavPropertyTables.EntityTag(row.IcsHash);

    private static DavWriteOutcome Refused(DavWriteStatus status) => new(status, null, null, 0);

    /// <summary>The status each guard's precondition earns, the precondition riding along so the
    /// XML answer names the one that was judged.</summary>
    private static DavWriteOutcome Refused(IcsProblem problem) =>
        new(problem.Precondition switch
        {
            IcsPrecondition.MaxResourceSize => DavWriteStatus.TooLarge,
            IcsPrecondition.SupportedCalendarData => DavWriteStatus.UnsupportedVersion,
            IcsPrecondition.SupportedCalendarComponent => DavWriteStatus.UnsupportedComponent,
            IcsPrecondition.MaxInstances => DavWriteStatus.TooManyInstances,
            _ => DavWriteStatus.InvalidCard,
        }, null, null, 0, problem.Precondition);

    private static readonly DavWriteOutcome Emptied = new(DavWriteStatus.Deleted, null, null, 0);

    private static DavWriteOutcome Conflict(Guid userId, string calendarName, string holderName) =>
        new(DavWriteStatus.UidConflict, null, DavPaths.Event(userId, calendarName, holderName), 0);
}
