using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Calendar;

namespace weesky.Snoopy.Microservice.Repositories;

/// <inheritdoc cref="ICalendarStore"/>
internal sealed partial class CalendarStore(PreferencesDbContext context, ICalendarSyncStore sync)
    : ICalendarStore
{
    /// <summary>
    /// What bounds the sidebar. Far above real use — twenty collections is already a list nobody
    /// reads — and there to stop a scripted caller from making the window query fan out over
    /// thousands of collections on every page load.
    /// </summary>
    internal const int MaxPerUser = 20;

    /// <summary>The one <c>dav_name</c> that is not a GUID, and the one collection no deletion may
    /// take: a user with no calendar has nowhere to write.</summary>
    internal const string DefaultDavName = "default";

    /// <summary>
    /// One transaction, one rank — but not one deletion, one rank. Each event removed here is
    /// ARCHIVED first, so a collection of five thousand in a single transaction would write
    /// gigabytes of MEDIUMTEXT: a redo log that overflows, and the state row's lock held long
    /// enough for every phone to come back in 503.
    /// </summary>
    internal const int DeleteBatch = 100;

    /// <summary>The name a calendar born of <see cref="EnsureDefaultAsync"/> carries until the user
    /// renames it. English, like every other label the webmail ships.</summary>
    private const string DefaultDisplayName = "Personal";

    // Interpolated, not spelled out, so the ceiling is written once.
    internal static readonly string CapReached =
        $"You have reached the maximum of {MaxPerUser} calendars";

    internal const string NotDeletable = "The default calendar cannot be deleted";

    internal const string NotFound = "Calendar not found";

    /// <summary>
    /// The colour goes out verbatim on the export's <c>COLOR</c> line, so anything but six hex
    /// digits is refused here rather than left to forge an iCalendar line further down.
    /// </summary>
    internal const string BadColour = "A calendar colour is an #RRGGBB value";

    /// <summary>#RRGGBB, or Apple's #RRGGBBAA whose alpha channel is dropped on write.</summary>
    [GeneratedRegex("^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$")]
    private static partial Regex ColourShape();

    public async Task<IReadOnlyList<CalendarView>> ListAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var rows = await context.Calendars.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Order).ThenBy(c => c.DisplayName)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(View)];
    }

    public async Task<CalendarView> EnsureDefaultAsync(
        Guid userId, string browserTimeZone, CancellationToken cancellationToken)
    {
        var held = await FindByNameAsync(userId, DefaultDavName, cancellationToken);
        if (held is not null) return View(held);

        var row = new Calendar
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DavName = DefaultDavName,
            DisplayName = DefaultDisplayName,
            Description = string.Empty,
            Color = CalendarPalette.Colours[0],
            Order = 0,
            TimeZone = browserTimeZone,
            IsVisible = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            return View(await AddAsync(row, cancellationToken));
        }
        catch (DbUpdateException)
        {
            // Two first requests raced: the unique index on (user_id, dav_name) named the winner,
            // and "ensure" owes its caller that row rather than the loser's exception.
            context.ChangeTracker.Clear();
            var winner = await FindByNameAsync(userId, DefaultDavName, cancellationToken);
            if (winner is null) throw;

            return View(winner);
        }
    }

    public async Task<Result<Guid>> CreateAsync(
        Guid userId, CalendarWrite write, string browserTimeZone, CancellationToken cancellationToken)
    {
        var colour = Colour(write.Color);
        if (write.Color is not null && colour is null) return Result.Failure<Guid>(BadColour);

        // The cap counted INSIDE the transaction, like CalendarEventStore counts its own: a refusal
        // and a creation must not be decided from two different reads of the same table.
        return await InTransactionAsync<Result<Guid>>(async () =>
        {
            var held = await context.Calendars.AsNoTracking()
                .Where(c => c.UserId == userId)
                .Select(c => c.Order)
                .ToListAsync(cancellationToken);
            if (held.Count >= MaxPerUser) return Result.Failure<Guid>(CapReached);

            var id = Guid.NewGuid();
            var row = new Calendar
            {
                Id = id,
                UserId = userId,
                // The id and not a slug of the name: a client syncs on this segment and it is never
                // renamed, so it must not be derived from anything the user can change.
                DavName = id.ToString(),
                DisplayName = write.DisplayName,
                Description = write.Description ?? string.Empty,
                Color = colour ?? CalendarPalette.Next(held.Count),
                Order = write.Order ?? (held.Count == 0 ? 0 : held.Max() + 1),
                TimeZone = browserTimeZone,
                IsVisible = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.Calendars.Add(row);
            await context.SaveChangesAsync(cancellationToken);
            await sync.CreateStateAsync(row.Id, cancellationToken);
            return Result.Success(id);
        }, cancellationToken);
    }

    public async Task<Result> UpdateAsync(
        Guid userId, Guid calendarId, CalendarWrite write, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, calendarId, cancellationToken);
        if (row is null) return Result.Failure(NotFound);

        if (write.Color is not null)
        {
            if (Colour(write.Color) is not { } colour) return Result.Failure(BadColour);
            row.Color = colour;
        }

        row.DisplayName = write.DisplayName;
        if (write.Description is not null) row.Description = write.Description;
        if (write.Order is { } order) row.Order = order;
        row.UpdatedAt = DateTime.UtcNow;

        // No rank and no transaction: a colour is not a resource, and advancing the counter here
        // would make every phone resync a collection nothing in it changed.
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SetVisibleAsync(
        Guid userId, Guid calendarId, bool visible, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, calendarId, cancellationToken);
        if (row is null) return Result.Failure(NotFound);

        row.IsVisible = visible;
        row.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(
        Guid userId, Guid calendarId, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, calendarId, cancellationToken);
        if (row is null) return Result.Failure(NotFound);
        if (row.DavName == DefaultDavName) return Result.Failure(NotDeletable);

        // The ids first, so an empty collection opens no transaction and spends no rank at all.
        var doomed = await context.CalendarEvents
            .Where(e => e.CalendarId == calendarId)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        // Batched, and each batch its own transaction under its own rank: the ranks are spent on a
        // collection that is about to disappear, which costs nothing — where one transaction over
        // five thousand archived resources costs a redo log.
        foreach (var chunk in doomed.Chunk(DeleteBatch))
        {
            // A List, not the chunk array: EF's InMemory translator cannot funclet an array's
            // span-based Contains, which C#'s extension resolution now prefers.
            var ids = chunk.ToList();

            await InTransactionAsync(async () =>
            {
                // The state row's lock FIRST, as every other transaction of these two stores takes
                // it, so no door of theirs can deadlock against another. The rank itself is spent
                // on a collection about to disappear, which is why nothing here reads it back.
                await sync.NextSequenceAsync(calendarId, cancellationToken);

                // Read under the lock, so what is archived is what is being removed.
                var batch = await context.CalendarEvents
                    .Where(e => e.CalendarId == calendarId && ids.Contains(e.Id))
                    .ToListAsync(cancellationToken);

                foreach (var stored in batch)
                {
                    // EventId NULL: a delete revision outlives the row it describes, and CalendarId
                    // survives on purpose — calendar_revisions carries no FK, so the archive is not
                    // cascaded away by the very deletion that wrote it (décision 2).
                    await sync.ArchiveAsync(
                        userId, calendarId, null, stored.Uid, stored.DavName, stored.IcsRaw,
                        RevisionCause.Delete, cancellationToken);
                }

                // The InMemory provider enforces no foreign key, so the children go by hand: this is
                // what makes it behave like the cascade MariaDB actually runs.
                context.CalendarAttendees.RemoveRange(
                    await context.CalendarAttendees.Where(a => ids.Contains(a.EventId))
                        .ToListAsync(cancellationToken));
                context.CalendarEvents.RemoveRange(batch);
                await context.SaveChangesAsync(cancellationToken);

                // No tombstone per event: the whole collection goes, and a client that loses the
                // collection loses everything under it without being told name by name.
                return batch.Count;
            }, cancellationToken);
        }

        return await InTransactionAsync<Result>(async () =>
        {
            // The state row's lock FIRST here too, before the tombstones: this tail runs against the
            // same two tables a concurrent CalendarEventStore.DeleteAsync touches, and taking them
            // in the opposite order is the one way these two doors can deadlock.
            await sync.NextSequenceAsync(calendarId, cancellationToken);

            context.CalendarTombstones.RemoveRange(
                await context.CalendarTombstones.Where(t => t.CalendarId == calendarId)
                    .ToListAsync(cancellationToken));
            if (await context.CalendarSyncStates.FindAsync([calendarId], cancellationToken) is { } state)
                context.CalendarSyncStates.Remove(state);
            context.Calendars.Remove(row);

            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }

    /// <summary>
    /// The creation both doors share: the row and its sync state in one transaction (décision 2),
    /// so a collection can never be visible without the counter its ctag is cut from.
    /// </summary>
    private Task<Calendar> AddAsync(Calendar row, CancellationToken cancellationToken) =>
        InTransactionAsync(async () =>
        {
            context.Calendars.Add(row);
            await context.SaveChangesAsync(cancellationToken);
            await sync.CreateStateAsync(row.Id, cancellationToken);
            return row;
        }, cancellationToken);

    /// <summary>The colour as it will be stored, or null when the text is not one. Apple's alpha
    /// channel is dropped and the digits are folded, so one colour has one spelling.</summary>
    private static string? Colour(string? value) =>
        value is not null && ColourShape().IsMatch(value.Trim())
            ? value.Trim()[..7].ToLowerInvariant()
            : null;

    private static CalendarView View(Calendar row) =>
        new(row.Id, row.DavName, row.DisplayName, row.Description, row.Color, row.Order,
            row.TimeZone, row.IsVisible, row.DavName == DefaultDavName);

    /// <summary>
    /// Scoped by user on purpose: a calendar belonging to somebody else must be indistinguishable
    /// from one that does not exist, so the controller can answer 404 without leaking it.
    /// </summary>
    private Task<Calendar?> FindAsync(
        Guid userId, Guid calendarId, CancellationToken cancellationToken) =>
        context.Calendars.FirstOrDefaultAsync(
            c => c.Id == calendarId && c.UserId == userId, cancellationToken);

    private Task<Calendar?> FindByNameAsync(
        Guid userId, string davName, CancellationToken cancellationToken) =>
        context.Calendars.FirstOrDefaultAsync(
            c => c.UserId == userId && c.DavName == davName, cancellationToken);

    /// <summary>
    /// One transaction, opened THROUGH the context's execution strategy, with
    /// <see cref="ContactStore"/>'s commit rule: a body answering a failed <c>Result</c> leaves it
    /// uncommitted, so a refusal decided after a rank was taken rolls that rank back rather than
    /// waking every client for nothing.
    /// </summary>
    private Task<T> InTransactionAsync<T>(Func<Task<T>> body, CancellationToken cancellationToken)
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
