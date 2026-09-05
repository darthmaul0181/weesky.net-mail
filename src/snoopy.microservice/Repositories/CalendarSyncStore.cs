using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services.Calendar;

namespace weesky.Snoopy.Microservice.Repositories;

/// <inheritdoc cref="ICalendarSyncStore"/>
internal sealed class CalendarSyncStore(PreferencesDbContext context) : ICalendarSyncStore
{
    /// <summary>
    /// What one <see cref="PruneAsync"/> pass may remove from each of its two tables. The sweep is
    /// unfiltered across the deployment and runs daily, so this is not a policy about what to keep
    /// — everything past the cap is taken by the next pass — but the bound that keeps one pass's
    /// footprint independent of how large a backlog grew while the sweeper was not running.
    /// </summary>
    internal const int MaxRowsPerSweep = 50_000;

    public async Task<ulong> NextSequenceAsync(Guid calendarId, CancellationToken cancellationToken)
    {
        // The one precondition prose alone cannot enforce: outside a transaction ExecuteSql* runs in
        // autocommit, the row's lock drops the instant the statement completes, and EF may hand the
        // connection back to the pool before the re-read below runs — which can then land on a
        // different physical connection and answer a rank two callers both hold.
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                $"{nameof(NextSequenceAsync)} must run inside a transaction the caller owns.");
        }

        // Raw SQL, and the only raw SQL here, for the reason ContactSyncStore gives at length: one
        // statement must create the row when the collection has none, take its exclusive lock and
        // advance the counter, all inside the caller's transaction. The epoch drawn on every call is
        // never rewritten — ON DUPLICATE KEY UPDATE touches seq alone.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO calendar_sync_state (calendar_id, epoch, seq, pruned_below)
             VALUES ({calendarId}, {Guid.NewGuid()}, 1, 0)
             ON DUPLICATE KEY UPDATE seq = seq + 1
             """,
            cancellationToken);

        // Re-read inside the same transaction, and as a scalar: materialising the entity would let a
        // CalendarSyncState already tracked in this context answer with its stale, pre-increment seq.
        return await context.CalendarSyncStates
            .Where(s => s.CalendarId == calendarId)
            .Select(s => s.Seq)
            .SingleAsync(cancellationToken);
    }

    public async Task CreateStateAsync(Guid calendarId, CancellationToken cancellationToken)
    {
        context.CalendarSyncStates.Add(new CalendarSyncState
        {
            CalendarId = calendarId, Epoch = Guid.NewGuid(), Seq = 0, PrunedBelow = 0
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<SyncState?> ReadStateAsync(Guid calendarId, CancellationToken cancellationToken) =>
        await context.CalendarSyncStates
            .Where(s => s.CalendarId == calendarId)
            .Select(s => new SyncState(s.Epoch, s.Seq, s.PrunedBelow))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task PlaceTombstoneAsync(
        Guid calendarId, string davName, ulong rank, CancellationToken cancellationToken)
    {
        // Upsert and not insert: the key is (calendar_id, dav_name), so a name deleted, recreated
        // and deleted again lands on an existing row, where a bare INSERT would fail that second
        // deletion on a duplicate key — in production, on data the user believes gone.
        var held = await context.CalendarTombstones.SingleOrDefaultAsync(
            t => t.CalendarId == calendarId && t.DavName == davName, cancellationToken);

        if (held is null)
        {
            context.CalendarTombstones.Add(new CalendarTombstone
            {
                CalendarId = calendarId, DavName = davName, SyncSequence = rank,
                DeletedAt = DateTime.UtcNow
            });
        }
        else
        {
            held.SyncSequence = rank;
            held.DeletedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task LiftTombstoneAsync(
        Guid calendarId, string davName, CancellationToken cancellationToken)
    {
        var held = await context.CalendarTombstones.SingleOrDefaultAsync(
            t => t.CalendarId == calendarId && t.DavName == davName, cancellationToken);
        if (held is null) return;

        context.CalendarTombstones.Remove(held);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task ArchiveAsync(
        Guid userId, Guid? calendarId, Guid? eventId, string? uid, string? davName, string icsRaw,
        RevisionCause cause, CancellationToken cancellationToken)
    {
        context.CalendarRevisions.Add(new CalendarRevision
        {
            UserId = userId,
            CalendarId = calendarId,
            EventId = eventId,
            Uid = uid,
            DavName = davName,
            IcsHash = IcsDocument.HashOf(icsRaw),
            IcsRaw = icsRaw,
            Cause = cause,
            ReplacedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<PruneOutcome> PruneAsync(
        DateTime tombstonesBefore, DateTime revisionsBefore, CancellationToken cancellationToken)
    {
        // Oldest first, and capped: the sweep is unfiltered across the whole deployment, so an
        // uncapped pass is one query whose result set is however many rows a backlog has grown to.
        // Ordering by DeletedAt is what makes the cap safe for the watermark below — rank and
        // deletion time move together per collection — so a cut by time takes a PREFIX of each
        // collection's tombstones and the highest it raises the watermark to was actually deleted.
        var doomed = await context.CalendarTombstones
            .Where(t => t.DeletedAt < tombstonesBefore)
            .OrderBy(t => t.DeletedAt)
            .Take(MaxRowsPerSweep)
            .ToListAsync(cancellationToken);

        var highest = doomed
            .GroupBy(t => t.CalendarId)
            .ToDictionary(g => g.Key, g => g.Max(t => t.SyncSequence));

        // Loaded once for every affected collection rather than one query per collection: the sweep
        // spans the whole deployment, so an N+1 here is an N+1 on the busiest maintenance path.
        var calendarIds = highest.Keys.ToList();
        var affected = await context.CalendarSyncStates
            .Where(s => calendarIds.Contains(s.CalendarId))
            .ToDictionaryAsync(s => s.CalendarId, cancellationToken);

        foreach (var (calendarId, rank) in highest)
        {
            // No state row means no epoch, which means no token was ever issued for this collection:
            // there is nothing a watermark could protect.
            if (!affected.TryGetValue(calendarId, out var state)) continue;

            state.PrunedBelow = Math.Max(state.PrunedBelow, rank);
        }

        context.CalendarTombstones.RemoveRange(doomed);

        // Keys alone, then stubs to delete by. A revision carries the whole resource it archived —
        // up to IcsGuards.MaxIcsBytes — and this sweep spans every user, so materialising the
        // entities would put thirty days of the deployment's overwritten events on the heap at once,
        // for a DELETE that only ever needed the primary key.
        var stale = await context.CalendarRevisions
            .Where(r => r.ReplacedAt < revisionsBefore)
            .OrderBy(r => r.ReplacedAt)
            .Select(r => r.Id)
            .Take(MaxRowsPerSweep)
            .ToListAsync(cancellationToken);
        // A stub only where nothing is tracked under that key: an ArchiveAsync earlier in the same
        // scope may already hold that revision, and attaching a second instance throws on the
        // identity map. The sweeper opens its own scope, so this costs a lookup against nothing.
        var tracked = context.CalendarRevisions.Local.ToDictionary(r => r.Id);
        context.CalendarRevisions.RemoveRange(stale.Select(
            id => tracked.TryGetValue(id, out var held) ? held : new CalendarRevision { Id = id }));

        // One SaveChanges, so the watermark and the removal commit together or not at all. Split in
        // two, a process killed between them leaves the tombstones gone and pruned_below behind: a
        // stale token is then ACCEPTED, the response omits the deletion, and the client keeps the
        // event for ever. That is the hole pruned_below exists to close.
        await context.SaveChangesAsync(cancellationToken);

        return new PruneOutcome(doomed.Count, stale.Count,
            doomed.Count == MaxRowsPerSweep || stale.Count == MaxRowsPerSweep);
    }
}
