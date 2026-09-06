using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Repositories;

/// <inheritdoc cref="ICalendarEventStore"/>
internal sealed class CalendarEventStore(
    PreferencesDbContext context, ICalendarSyncStore sync, ILogger<CalendarEventStore> logger)
    : ICalendarEventStore
{
    /// <summary>What bounds one collection. Far above real use — it guards against a runaway
    /// import, not against a user.</summary>
    internal const int MaxPerCalendar = 5000;

    /// <summary>What one uploaded file may weigh, judged before a single byte of it is parsed:
    /// parsing is the work an oversized body is trying to make us do.</summary>
    internal const int MaxImportBytes = 20 * 1024 * 1024;

    /// <summary>One transaction, one rank. Every replacement archives what it overwrites, so a
    /// whole-file import in a single transaction would write gigabytes of MEDIUMTEXT.</summary>
    internal const int ImportBatch = 100;

    /// <summary>What a search may expand. The LIKE is bounded here, before any expansion: the cost
    /// of a search is a rule evaluation per row, not a string comparison.</summary>
    internal const int SearchLimit = 200;

    /// <summary>How far ahead a search looks for the next occurrence of a live series. A year and a
    /// day covers every rule the editor offers; the wider second pass below catches the rest.</summary>
    private const int SearchHorizonDays = 400;

    internal static readonly string CapReached =
        $"This calendar has reached the maximum of {MaxPerCalendar} events";

    internal static readonly string FileTooLarge =
        $"The file exceeds {MaxImportBytes / 1024 / 1024} MB";

    internal const string NotFound = "Event not found";

    /// <summary>The editor sent back a hash that is no longer the resource's. Its own message
    /// because 409 and 404 are two different stories for the screen: one reloads, the other closes.</summary>
    internal const string EventMoved =
        "The event changed since it was read. Reload it and try again.";

    internal const string NoStart = "The event carries no start";

    /// <summary>A collection is a property of the whole resource, not of one instance: moving a
    /// single occurrence would have to split the series, which is not what the gesture says.</summary>
    internal const string MoveNeedsWholeEvent = "Change the calendar with scope All";

    /// <summary>What one window answers at most. Refused, never truncated: a grid silently missing
    /// half its instances is worse than one told to ask for less.</summary>
    internal const int MaxWindowOccurrences = 20_000;

    internal const string WindowTooDense = "The window holds too many occurrences; narrow it";

    /// <summary>A UID is unique per collection (RFC 4791 § 4.1), so a move onto a collection that
    /// already answers to it is refused rather than left to fail on the index.</summary>
    internal const string UidTaken = "The target calendar already holds an event with this identifier";

    /// <summary>The window query's slack on both sides. All-day membership is decided by the
    /// expander, on dates; the columns hold instants placed in the calendar's zone, and the two
    /// readings differ by less than a day.</summary>
    private static readonly TimeSpan Margin = TimeSpan.FromDays(1);

    public async Task<Result<IReadOnlyList<EventOccurrence>>> WindowAsync(
        Guid userId, DateTime fromUtc, DateTime toUtc, string viewTimeZone,
        CancellationToken cancellationToken)
    {
        var lower = Shift(fromUtc, -Margin);
        var upper = Shift(toUtc, Margin);
        var rows = await context.CalendarEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.FirstOccurrence < upper && e.LastOccurrence > lower)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return Result.Success<IReadOnlyList<EventOccurrence>>([]);

        var zones = await ZonesAsync(userId, cancellationToken);
        var found = new List<EventOccurrence>();
        foreach (var row in rows)
        {
            if (IcsDocument.TryLoad(row.IcsRaw) is not { } parsed)
            {
                logger.LogWarning(
                    "Event {EventId} no longer parses and was left out of the window", row.Id);
                continue;
            }

            found.AddRange(OccurrenceExpander.Expand(
                row.Id, row.CalendarId, parsed, fromUtc, toUtc,
                zones.GetValueOrDefault(row.CalendarId, IcsTimeZones.Utc), viewTimeZone));

            // Counted as the rows are expanded and stopped at the first excess: a budget checked
            // only at the end would have paid for the whole answer before refusing it.
            if (found.Count > MaxWindowOccurrences)
                return Result.Failure<IReadOnlyList<EventOccurrence>>(WindowTooDense);
        }

        return Result.Success<IReadOnlyList<EventOccurrence>>([.. found.OrderBy(At)]);
    }

    public async Task<EventDetail?> GetAsync(
        Guid userId, Guid eventId, CancellationToken cancellationToken)
    {
        var row = await context.CalendarEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eventId && e.UserId == userId, cancellationToken);
        if (row is null || IcsDocument.TryLoad(row.IcsRaw) is not { } parsed) return null;

        var attendees = await context.CalendarAttendees.AsNoTracking()
            .Where(a => a.EventId == eventId)
            .OrderBy(a => a.Position)
            .Select(a => new AttendeeProjection(
                a.RecurrenceId, a.Email, a.Name, a.Role, a.PartStat, a.IsOrganizer))
            .ToListAsync(cancellationToken);

        return new EventDetail(
            row.Id, row.CalendarId, row.Uid, row.IcsHash, IcsReader.Read(parsed, row.CalendarId),
            IcsDocument.MasterOf(parsed)?.RecurrenceRule?.ToString(), attendees, row.Status,
            IcsReader.RepeatIsExact(parsed), IcsReader.ForeignAlarms(parsed));
    }

    public async Task<Result<Guid>> CreateAsync(
        Guid userId, EventWrite write, CancellationToken cancellationToken)
    {
        var calendar = await FindCalendarAsync(userId, write.CalendarId, cancellationToken);
        if (calendar is null) return Result.Failure<Guid>(CalendarStore.NotFound);

        var id = Guid.NewGuid();
        // An event born here has no foreign UID, so its own id serves — as ContactStore does. The
        // column stays distinct from the key because an imported resource brings one we must keep.
        var composed = Attempt(() => IcsComposer.ComposeNew(write, id.ToString(), DateTime.UtcNow));
        if (composed.IsFailure) return Result.Failure<Guid>(composed.Error);

        var parsed = Parse(composed.Value);
        if (parsed.IsFailure) return Result.Failure<Guid>(parsed.Error);

        return await InTransactionAsync(async () =>
        {
            var rank = await sync.NextSequenceAsync(calendar.Id, cancellationToken);

            // Counted under the state lock, as UpdateAsync counts its own: a refusal and a creation
            // must not be decided from two different reads of the same table.
            if (await context.CalendarEvents.CountAsync(e => e.CalendarId == calendar.Id, cancellationToken)
                >= MaxPerCalendar)
                return Result.Failure<Guid>(CapReached);

            var row = new CalendarEvent
            {
                Id = id, CalendarId = calendar.Id, UserId = userId, DavName = $"{id}.ics"
            };
            context.CalendarEvents.Add(row);
            await ApplyIcsAsync(row, calendar, composed.Value, parsed.Value, rank, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            // A name that comes back must stop being reported as deleted. Always a miss today — the
            // name is a fresh GUID — and it stays for the door a CalDAV PUT will open.
            await sync.LiftTombstoneAsync(calendar.Id, row.DavName, cancellationToken);
            return Result.Success(id);
        }, cancellationToken);
    }

    public async Task<Result> UpdateAsync(
        Guid userId, Guid eventId, EditScope scope, string? instanceId, EventWrite write,
        string? ifHash, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, eventId, cancellationToken);
        if (row is null) return Result.Failure(NotFound);

        // Refused before anything else: a client that says what it read is refused when that is no
        // longer true, and the refusal opens no transaction, takes no rank and wakes no client.
        if (ifHash is not null && ifHash != row.IcsHash) return Result.Failure(EventMoved);

        var source = await FindCalendarAsync(userId, row.CalendarId, cancellationToken);
        var target = write.CalendarId == row.CalendarId
            ? source
            : await FindCalendarAsync(userId, write.CalendarId, cancellationToken);
        if (source is null || target is null) return Result.Failure(NotFound);

        var moving = target.Id != source.Id;
        var followingId = Guid.NewGuid();

        // Composed outside the lock so an invalid write, an unreadable resource or a save that
        // changes nothing is refused without opening a transaction — and composed AGAIN inside it
        // when the row moved, since the rewrite must build on the bytes actually stored.
        var rewrite = Compose(row.IcsRaw, scope, instanceId, write, followingId);
        if (rewrite.IsFailure) return Result.Failure(rewrite.Error);
        if (moving && rewrite.Value.Scope != EditScope.All) return Result.Failure(MoveNeedsWholeEvent);
        if (!moving && rewrite.Value.Unchanged) return Result.Success();

        var read = row.IcsHash;

        return await InTransactionAsync<Result>(async () =>
        {
            var (sourceRank, targetRank) = await RanksAsync(source.Id, target.Id, cancellationToken);

            // Re-read under the state lock. Without it two saves holding the same still-valid hash
            // both pass the check above and the second silently overwrites the first, both archiving
            // the same pre-image — the lost update ContactStore.UpdateAsync closes the same way.
            if (!await ReloadAsync(row, cancellationToken)) return Result.Failure(NotFound);
            // Moved collection since the read: the ranks just taken are not the ones this write
            // needs, and a pure move leaves the bytes alone — so the hash below would not catch it.
            if (row.CalendarId != source.Id) return Result.Failure(EventMoved);
            if (row.IcsHash != read)
            {
                if (ifHash is not null) return Result.Failure(EventMoved);

                rewrite = Compose(row.IcsRaw, scope, instanceId, write, followingId);
                if (rewrite.IsFailure) return Result.Failure(rewrite.Error);
                if (moving && rewrite.Value.Scope != EditScope.All) return Result.Failure(MoveNeedsWholeEvent);
                if (!moving && rewrite.Value.Unchanged) return Result.Success();
            }

            if (moving && await context.CalendarEvents.AnyAsync(
                    e => e.CalendarId == target.Id && e.Uid == row.Uid, cancellationToken))
                return Result.Failure(UidTaken);

            // What this write ADDS to the target: the resource itself when it changes collection,
            // and the following half when the series is cut in two. Counted under the lock, or a
            // move and a split racing each other both believe the last place is theirs.
            var arriving = (moving ? 1 : 0) + (rewrite.Value.Following is null ? 0 : 1);
            if (arriving > 0 && await context.CalendarEvents.CountAsync(
                    e => e.CalendarId == target.Id, cancellationToken) + arriving > MaxPerCalendar)
                return Result.Failure(CapReached);

            await sync.ArchiveAsync(
                userId, source.Id, row.Id, row.Uid, row.DavName, row.IcsRaw,
                RevisionCause.Webmail, cancellationToken);

            if (moving)
            {
                await sync.PlaceTombstoneAsync(source.Id, row.DavName, sourceRank, cancellationToken);
                row.CalendarId = target.Id;
                // Décision 2: the name follows the resource and is renamed only where the target
                // already answers to it — a client syncs on it, so moving it costs a resync.
                row.DavName = await FreeNameAsync(target.Id, row.Id, row.DavName, cancellationToken);
            }

            await ApplyIcsAsync(
                row, target, rewrite.Value.Ics, rewrite.Value.Parsed, targetRank, cancellationToken);

            if (rewrite.Value is { Following: { } text, FollowingParsed: { } half })
            {
                var born = new CalendarEvent
                {
                    Id = followingId, CalendarId = target.Id, UserId = userId,
                    DavName = $"{followingId}.ics"
                };
                context.CalendarEvents.Add(born);
                // The same rank as the original: one gesture, one version of the collection.
                await ApplyIcsAsync(born, target, text, half, targetRank, cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
            if (moving) await sync.LiftTombstoneAsync(target.Id, row.DavName, cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }

    public async Task<Result> DeleteAsync(
        Guid userId, Guid eventId, EditScope scope, string? instanceId,
        CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, eventId, cancellationToken);
        if (row is null) return Result.Failure(NotFound);

        var calendar = await FindCalendarAsync(userId, row.CalendarId, cancellationToken);
        if (calendar is null) return Result.Failure(NotFound);

        // Resolved outside the lock so an invalid instance id or a deletion that removes nothing is
        // refused without opening a transaction; resolved again inside it when the row moved.
        var removal = Removing(row.IcsRaw, scope, instanceId, calendar.Id);
        if (removal.IsFailure) return Result.Failure(removal.Error);
        if (removal.Value.Unchanged) return Result.Success();

        var read = row.IcsHash;

        return await InTransactionAsync<Result>(async () =>
        {
            var rank = await sync.NextSequenceAsync(row.CalendarId, cancellationToken);

            // Read under the lock, so what is archived is what is actually being removed.
            if (!await ReloadAsync(row, cancellationToken)) return Result.Failure(NotFound);
            if (row.CalendarId != calendar.Id) return Result.Failure(EventMoved);
            if (row.IcsHash != read)
            {
                removal = Removing(row.IcsRaw, scope, instanceId, calendar.Id);
                if (removal.IsFailure) return Result.Failure(removal.Error);
                if (removal.Value.Unchanged) return Result.Success();
            }

            if (removal.Value.Whole)
            {
                // EventId NULL: a delete revision outlives the row it describes.
                await sync.ArchiveAsync(
                    userId, row.CalendarId, null, row.Uid, row.DavName, row.IcsRaw,
                    RevisionCause.Delete, cancellationToken);

                await ClearAttendeesAsync(row.Id, cancellationToken);
                context.CalendarEvents.Remove(row);
                await context.SaveChangesAsync(cancellationToken);

                await sync.PlaceTombstoneAsync(row.CalendarId, row.DavName, rank, cancellationToken);
                return Result.Success();
            }

            await sync.ArchiveAsync(
                userId, row.CalendarId, row.Id, row.Uid, row.DavName, row.IcsRaw,
                RevisionCause.Webmail, cancellationToken);

            await ApplyIcsAsync(
                row, calendar, removal.Value.Ics, removal.Value.Parsed!, rank, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<EventOccurrence>> SearchAsync(
        Guid userId, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var pattern = $"%{Escaped(text.Trim())}%";
        var rows = await context.CalendarEvents.AsNoTracking()
            .Where(e => e.UserId == userId
                && (EF.Functions.Like(e.Summary!, pattern, LikeEscape)
                    || EF.Functions.Like(e.Location!, pattern, LikeEscape)
                    || EF.Functions.Like(e.Description!, pattern, LikeEscape)))
            // Newest last occurrence first, because the cut falls HERE, before any expansion: order
            // by the start and a book with two hundred old events hides every series still running,
            // which is the only kind a search is usually looking for.
            .OrderByDescending(e => e.LastOccurrence)
            .Take(SearchLimit)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return [];

        var zones = await ZonesAsync(userId, cancellationToken);
        var now = DateTime.UtcNow;
        var found = new List<EventOccurrence>();
        foreach (var row in rows)
        {
            if (IcsDocument.TryLoad(row.IcsRaw) is not { } parsed)
            {
                logger.LogWarning("Event {EventId} no longer parses and was left out of the search", row.Id);
                continue;
            }

            var zone = zones.GetValueOrDefault(row.CalendarId, IcsTimeZones.Utc);
            if (Nearest(row, parsed, zone, now) is { } occurrence) found.Add(occurrence);
        }

        // Ordered on the occurrence, not on the row: what a result answers with is the instance the
        // reader will look at, and "soonest first" is the only order that means anything here.
        return [.. found.OrderBy(At)];
    }

    public Task<CalendarImportOutcome> ImportAsync(
        Guid userId, Guid calendarId, string vcalendar, CancellationToken cancellationToken) =>
        Importer().ImportAsync(userId, calendarId, vcalendar, cancellationToken);

    public Task<string> ExportAsync(
        Guid userId, Guid calendarId, CancellationToken cancellationToken) =>
        Importer().ExportAsync(userId, calendarId, cancellationToken);

    /// <summary>The import and export half, split off so this file stays readable; it writes
    /// through the very same gate, transaction wrapper and context.</summary>
    private CalendarEventImporter Importer() => new(this, context, sync);

    /// <summary>
    /// The one place <c>ics_raw</c>, its hash, the index over it and the attendee rows are written.
    /// A hash computed by a caller is a hash a caller will forget, and a column posed beside the
    /// resource rather than projected from it is a column that drifts from it.
    /// </summary>
    internal async Task ApplyIcsAsync(
        CalendarEvent row, Calendar calendar, string ics, IcsCalendar parsed, ulong rank,
        CancellationToken cancellationToken)
    {
        var projection = IcsProjector.Project(parsed, calendar.TimeZone);
        if (projection.UnknownTimeZone)
        {
            logger.LogWarning(
                "Time zone {Tzid} is unknown; the time is treated as floating",
                IcsDocument.MasterOf(parsed)?.DtStart?.TzId);
        }

        row.IcsRaw = ics;
        row.IcsHash = IcsDocument.HashOf(ics);
        row.Uid = projection.Uid;
        row.Summary = projection.Summary;
        row.Location = projection.Location;
        row.Description = projection.Description;
        row.StartsAt = projection.StartsAt;
        row.EndsAt = projection.EndsAt;
        row.IsAllDay = projection.IsAllDay;
        row.TimeZone = projection.TimeZone;
        row.IsRecurring = projection.IsRecurring;
        row.FirstOccurrence = projection.FirstOccurrence;
        row.LastOccurrence = projection.LastOccurrence;
        row.Status = projection.Status;
        row.Transparency = projection.Transparency;
        row.Class = projection.Class;
        row.SyncSequence = rank;
        row.UpdatedAt = DateTime.UtcNow;
        if (string.IsNullOrEmpty(row.DavName)) row.DavName = $"{row.Id}.ics";

        // Total and destructive, like the vCard projection: one that updated what changed would
        // drift from the resource in silence. A row not in the database yet has nothing to clear.
        if (context.Entry(row).State is EntityState.Unchanged or EntityState.Modified)
            await ClearAttendeesAsync(row.Id, cancellationToken);

        var position = 0;
        foreach (var attendee in projection.Attendees)
        {
            context.CalendarAttendees.Add(new CalendarAttendee
            {
                EventId = row.Id,
                Position = position++,
                RecurrenceId = attendee.RecurrenceId,
                Email = attendee.Email,
                Name = attendee.Name,
                Role = attendee.Role,
                PartStat = attendee.PartStat,
                IsOrganizer = attendee.IsOrganizer
            });
        }
    }

    private async Task ClearAttendeesAsync(Guid eventId, CancellationToken cancellationToken) =>
        context.CalendarAttendees.RemoveRange(
            await context.CalendarAttendees.Where(a => a.EventId == eventId)
                .ToListAsync(cancellationToken));

    /// <summary>
    /// Every gate a stored resource passes, in the order that names the real cause. Webmail writes
    /// cannot exceed the density the composer offers, so running the two costs nothing and is what
    /// makes the invariant hold whatever the door.
    /// </summary>
    internal static Result<IcsCalendar> Parse(string ics)
    {
        if (IcsGuards.CheckSize(ics) is { } tooLarge) return Result.Failure<IcsCalendar>(tooLarge.Message);

        var parsed = IcsDocument.TryLoad(ics);
        if (IcsGuards.Check(ics, parsed) is { } invalid) return Result.Failure<IcsCalendar>(invalid.Message);
        if (IcsGuards.CheckDensity(parsed!) is { } dense) return Result.Failure<IcsCalendar>(dense.Message);
        if (IcsGuards.CheckExpansion(parsed!) is { } opaque) return Result.Failure<IcsCalendar>(opaque.Message);

        var master = IcsDocument.MasterOf(parsed!) ?? IcsDocument.Components(parsed!).First();
        return master.DtStart is null ? Result.Failure<IcsCalendar>(NoStart) : Result.Success(parsed!);
    }

    /// <summary>An invalid <see cref="EventWrite"/> throws out of the composer; here it is one
    /// refusal among the others, carrying the composer's own words.</summary>
    internal static Result<T> Attempt<T>(Func<T> gesture)
    {
        try { return Result.Success(gesture()); }
        catch (ArgumentException failed) { return Result.Failure<T>(failed.Message); }
    }

    /// <summary>Reloads a row read before the state lock; false when it no longer exists.</summary>
    private async Task<bool> ReloadAsync(CalendarEvent row, CancellationToken cancellationToken)
    {
        var entry = context.Entry(row);
        await entry.ReloadAsync(cancellationToken);
        return entry.State is not EntityState.Detached;
    }

    /// <summary>
    /// The scope a resource can actually honour. A narrow scope on a series that does not repeat,
    /// and a cut at or before the master's own start, are the whole event: anything else duplicates
    /// the row under a second UID, or deletes nothing at all and answers success.
    /// </summary>
    private static EditScope Narrowed(EditScope scope, IcsCalendar held, string? instanceId)
    {
        if (scope == EditScope.All) return scope;

        var master = IcsDocument.MasterOf(held);
        if (master is null) return EditScope.All;
        if (master.RecurrenceRule is null && master.RecurrenceDates?.GetAllDates().Any() != true)
            return EditScope.All;
        if (scope == EditScope.This) return scope;

        return IcsDocument.InstanceOf(master, instanceId ?? string.Empty) is { } at
               && master.DtStart is { } start
               && IcsComposer.Instant(held, at) <= IcsComposer.Instant(held, start)
            ? EditScope.All
            : scope;
    }

    /// <summary>
    /// One edit resolved: the resource as it will be rewritten, and the second resource a cut leaves
    /// behind. Held apart from the write so that a row found to have moved under the lock can be
    /// recomposed on the bytes actually stored.
    /// </summary>
    private sealed record Rewrite(
        IcsCalendar Held, EditScope Scope, string Ics, IcsCalendar Parsed, string? Following,
        IcsCalendar? FollowingParsed)
    {
        /// <summary>Décision 4: a save that says nothing new buys no revision, no rank and no
        /// client woken. A cut always writes — it creates a resource.</summary>
        internal bool Unchanged => Following is null && IcsComposer.SameContent(Held, Parsed);
    }

    private static Result<Rewrite> Compose(
        string icsRaw, EditScope scope, string? instanceId, EventWrite write, Guid followingId)
    {
        if (IcsDocument.TryLoad(icsRaw) is not { } held) return Result.Failure<Rewrite>(NotFound);

        var now = DateTime.UtcNow;
        var narrowed = Narrowed(scope, held, instanceId);
        var composed = Attempt(() => narrowed switch
        {
            EditScope.This => new Composition(
                IcsComposer.RewriteOne(held, instanceId ?? string.Empty, write, now), null),
            EditScope.ThisAndFollowing => Halves(IcsComposer.Split(
                held, instanceId ?? string.Empty, write, followingId.ToString(), now)),
            _ => new Composition(IcsComposer.RewriteAll(held, write, now), null),
        });
        if (composed.IsFailure) return Result.Failure<Rewrite>(composed.Error);

        var parsed = Parse(composed.Value.Ics);
        if (parsed.IsFailure) return Result.Failure<Rewrite>(parsed.Error);

        IcsCalendar? half = null;
        if (composed.Value.Following is { } text)
        {
            var following = Parse(text);
            if (following.IsFailure) return Result.Failure<Rewrite>(following.Error);
            half = following.Value;
        }

        return Result.Success(new Rewrite(
            held, narrowed, composed.Value.Ics, parsed.Value, composed.Value.Following, half));
    }

    /// <summary>
    /// One deletion resolved. <c>Whole</c> says the scope narrowed to the entire resource, and
    /// <c>Ics</c>/<c>Parsed</c> then repeat what is stored — nothing is composed, the row goes.
    /// </summary>
    private sealed record Removal(bool Whole, IcsCalendar? Held, string Ics, IcsCalendar? Parsed)
    {
        internal bool Unchanged => !Whole && IcsComposer.SameContent(Held!, Parsed!);
    }

    private static Result<Removal> Removing(
        string icsRaw, EditScope scope, string? instanceId, Guid calendarId)
    {
        // Scope All is the whole resource whatever its bytes say — including bytes that no longer
        // parse. A row can only ever be deleted or archived from what is stored, never composed, so
        // an unparsable ics_raw must not block the one scope that never reads the document.
        if (scope == EditScope.All) return Result.Success(new Removal(true, null, icsRaw, null));

        if (IcsDocument.TryLoad(icsRaw) is not { } held) return Result.Failure<Removal>(NotFound);

        var narrowed = Narrowed(scope, held, instanceId);
        if (narrowed == EditScope.All) return Result.Success(new Removal(true, held, icsRaw, held));

        var now = DateTime.UtcNow;
        var composed = Attempt(() => narrowed == EditScope.This
            ? IcsComposer.RemoveOne(held, instanceId ?? string.Empty, now)
            // The write is the resource read back: only the original half is kept, so what the
            // following half would have said never reaches storage.
            : IcsComposer.Split(held, instanceId ?? string.Empty, IcsReader.Read(held, calendarId),
                Guid.NewGuid().ToString(), now).Original);
        if (composed.IsFailure) return Result.Failure<Removal>(composed.Error);

        var parsed = Parse(composed.Value);
        return parsed.IsFailure
            ? Result.Failure<Removal>(parsed.Error)
            : Result.Success(new Removal(false, held, composed.Value, parsed.Value));
    }

    private sealed record Composition(string Ics, string? Following);

    private static Composition Halves(SplitOutcome outcome) =>
        new(outcome.Original, outcome.Following);

    /// <summary>
    /// The two ranks a move needs, always taken in ascending calendar id: two callers moving
    /// resources in opposite directions between the same pair would otherwise take the two state
    /// locks in opposite orders and deadlock.
    /// </summary>
    private async Task<(ulong Source, ulong Target)> RanksAsync(
        Guid source, Guid target, CancellationToken cancellationToken)
    {
        if (source == target)
        {
            var only = await sync.NextSequenceAsync(source, cancellationToken);
            return (only, only);
        }

        if (source.CompareTo(target) < 0)
        {
            var first = await sync.NextSequenceAsync(source, cancellationToken);
            return (first, await sync.NextSequenceAsync(target, cancellationToken));
        }

        var second = await sync.NextSequenceAsync(target, cancellationToken);
        return (await sync.NextSequenceAsync(source, cancellationToken), second);
    }

    private async Task<string> FreeNameAsync(
        Guid calendarId, Guid eventId, string davName, CancellationToken cancellationToken) =>
        await context.CalendarEvents.AnyAsync(
            e => e.CalendarId == calendarId && e.DavName == davName && e.Id != eventId,
            cancellationToken)
            ? $"{eventId}.ics"
            : davName;

    /// <summary>
    /// The occurrence a search result points at: the next one from now for a series still running,
    /// the last one it ever had for a series already over. The forward walk is bounded — a rule the
    /// editor offers always fires within a year, and the wider pass is for the rest.
    /// </summary>
    private static EventOccurrence? Nearest(
        CalendarEvent row, IcsCalendar parsed, string zone, DateTime now)
    {
        if (row.LastOccurrence <= now)
        {
            return Expand(Shift(row.LastOccurrence, -TimeSpan.FromDays(SearchHorizonDays)),
                Shift(row.LastOccurrence, Margin)).LastOrDefault();
        }

        var horizon = Shift(now, TimeSpan.FromDays(SearchHorizonDays));
        return Expand(now, Earlier(horizon, Shift(row.LastOccurrence, Margin))).FirstOrDefault()
               ?? Expand(now, Earlier(Shift(now, TimeSpan.FromDays(365 * OccurrenceExpander.MaxYears)),
                   Shift(row.LastOccurrence, Margin))).FirstOrDefault();

        IReadOnlyList<EventOccurrence> Expand(DateTime from, DateTime to) =>
            OccurrenceExpander.Expand(row.Id, row.CalendarId, parsed, from, to, zone, zone);
    }

    /// <summary>Every collection's zone in one query: the window expands each row in the zone of
    /// its own calendar, and a lookup per row would be an N+1 on the busiest read there is.</summary>
    private async Task<Dictionary<Guid, string>> ZonesAsync(
        Guid userId, CancellationToken cancellationToken) =>
        await context.Calendars.AsNoTracking()
            .Where(c => c.UserId == userId)
            .ToDictionaryAsync(c => c.Id, c => c.TimeZone, cancellationToken);

    private Task<CalendarEvent?> FindAsync(
        Guid userId, Guid eventId, CancellationToken cancellationToken) =>
        context.CalendarEvents.FirstOrDefaultAsync(
            e => e.Id == eventId && e.UserId == userId, cancellationToken);

    /// <summary>Scoped by user: a collection belonging to somebody else must be indistinguishable
    /// from one that does not exist.</summary>
    internal Task<Calendar?> FindCalendarAsync(
        Guid userId, Guid calendarId, CancellationToken cancellationToken) =>
        context.Calendars.FirstOrDefaultAsync(
            c => c.Id == calendarId && c.UserId == userId, cancellationToken);

    private const string LikeEscape = "\\";

    /// <summary>The three characters LIKE reads as syntax. Escaped rather than stripped: a user
    /// searching for "100%" means the sign, not "anything".</summary>
    private static string Escaped(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>The instant an occurrence is sorted on, whichever of the three shapes it has.</summary>
    private static DateTime At(EventOccurrence occurrence) =>
        occurrence.StartUtc
        ?? occurrence.StartDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
        ?? occurrence.LocalStart
        ?? DateTime.MinValue;

    private static DateTime Earlier(DateTime left, DateTime right) => left <= right ? left : right;

    private static DateTime Shift(DateTime at, TimeSpan margin) =>
        new(Math.Clamp(at.Ticks + margin.Ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks),
            DateTimeKind.Utc);

    /// <summary>
    /// One transaction, opened THROUGH the context's execution strategy, with
    /// <see cref="ContactStore"/>'s commit rule: a body answering a failed <c>Result</c> leaves it
    /// uncommitted, so a refusal decided after a rank was taken rolls that rank back.
    /// </summary>
    internal Task<T> InTransactionAsync<T>(Func<Task<T>> body, CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task<T>> operation = async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            var outcome = await body();
            if (outcome is CSharpFunctionalExtensions.IResult { IsFailure: true }) return outcome;

            await transaction.CommitAsync(token);
            return outcome;
        };
        return context.Database.CreateExecutionStrategy().ExecuteAsync(operation, cancellationToken);
    }
}
