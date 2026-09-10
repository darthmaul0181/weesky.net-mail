using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar;

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

    /// <summary>The unique index on (user_id, dav_name) refusing a URL segment a client chose:
    /// only <see cref="CreateNamedAsync"/> can meet it, the webmail naming a calendar by its own
    /// id.</summary>
    internal const string NameTaken = "This URL is already taken by another calendar";

    internal const string NotFound = "Calendar not found";

    /// <summary>
    /// The colour goes out verbatim on the export's <c>COLOR</c> line, so anything but six hex
    /// digits is refused here rather than left to forge an iCalendar line further down.
    /// </summary>
    internal const string BadColour = "A calendar colour is an #RRGGBB value";

    /// <summary>#RRGGBB, or Apple's #RRGGBBAA whose alpha channel is dropped on write.</summary>
    [GeneratedRegex("^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$")]
    internal static partial Regex ColourShape();

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

    public Task<Result<Guid>> CreateAsync(
        Guid userId, CalendarWrite write, string browserTimeZone, CancellationToken cancellationToken) =>
        // The id and not a slug of the name: a client syncs on this segment and it is never
        // renamed, so it must not be derived from anything the user can change.
        CreateRowAsync(userId, null, write, browserTimeZone, cancellationToken);

    public async Task<Result<Guid>> CreateNamedAsync(
        Guid userId, string davName, CalendarWrite write, CancellationToken cancellationToken)
    {
        // The zone of `default`, and UTC when the account holds none at all — a hand-restored base
        // (§ 6), where a MKCALENDAR carries no browser to ask and inventing a zone would be worse.
        var fallback = (await FindByNameAsync(userId, DefaultDavName, cancellationToken))?.TimeZone
            ?? IcsTimeZones.Utc;

        return await CreateRowAsync(userId, davName, write, fallback, cancellationToken);
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
        if (write.TimeZone is not null) row.TimeZone = write.TimeZone;
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

            // No tombstone per event: the whole collection goes, and a client that loses the
            // collection loses everything under it without being told name by name.
            await InTransactionAsync(
                () => CalendarBatchDelete.RunAsync(context, sync, userId, calendarId, ids,
                    tombstones: false, cancellationToken),
                cancellationToken);
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
    /// The creation both façades share (décision 2): the cap counted INSIDE the transaction, like
    /// <see cref="CalendarEventStore"/> counts its own — a refusal and a creation must not be
    /// decided from two different reads of the same table — the palette's next colour, the last
    /// rank, and the state row, so a collection can never be visible without the counter its ctag
    /// is cut from.
    /// </summary>
    /// <param name="userId">the owner</param>
    /// <param name="davName">null when the collection is named by its own id, as the webmail's is</param>
    /// <param name="write">what the caller asks of the row</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <param name="fallbackZone">the zone to store when <c>write.TimeZone</c> names none</param>
    private async Task<Result<Guid>> CreateRowAsync(Guid userId, string? davName, CalendarWrite write,
        string fallbackZone, CancellationToken cancellationToken)
    {
        var colour = Colour(write.Color);
        if (write.Color is not null && colour is null) return Result.Failure<Guid>(BadColour);

        return await InTransactionAsync<Result<Guid>>(async () =>
        {
            var held = await context.Calendars.AsNoTracking()
                .Where(c => c.UserId == userId)
                .Select(c => new { c.Order, c.DavName })
                .ToListAsync(cancellationToken);
            if (held.Count >= MaxPerUser) return Result.Failure<Guid>(CapReached);
            if (davName is not null && held.Any(c => c.DavName == davName))
                return Result.Failure<Guid>(NameTaken);

            var id = Guid.NewGuid();
            var row = new Calendar
            {
                Id = id,
                UserId = userId,
                DavName = davName ?? id.ToString(),
                DisplayName = write.DisplayName,
                Description = write.Description ?? string.Empty,
                Color = colour ?? CalendarPalette.Next(held.Count),
                Order = write.Order ?? (held.Count == 0 ? 0 : held.Max(c => c.Order) + 1),
                TimeZone = write.TimeZone ?? fallbackZone,
                IsVisible = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            try
            {
                context.Calendars.Add(row);
                await context.SaveChangesAsync(cancellationToken);
            }
            // Only the DAV door can lose that race: the webmail's POST names by a fresh GUID and
            // cannot collide, so a write failure there is a real one and must not read as a taken URL.
            catch (DbUpdateException) when (davName is not null)
            {
                context.ChangeTracker.Clear();
                return Result.Failure<Guid>(NameTaken);
            }

            await sync.CreateStateAsync(row.Id, cancellationToken);
            return Result.Success(id);
        }, cancellationToken);
    }

    /// <summary>
    /// The default collection's own creation: no cap to count and no colour to choose, so it takes
    /// the row it was handed straight into the transaction its state row shares (décision 2).
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
    /// channel is dropped and the digits are folded, so one colour has one spelling. Internal
    /// because the CalDAV readers judge a client's colour through it: written twice, the two
    /// spellings of one value would drift.</summary>
    internal static string? Colour(string? value) =>
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
    private async Task<T> InTransactionAsync<T>(Func<Task<T>> body, CancellationToken cancellationToken)
    {
        // Reentrant: DavCredentialStore.EnableAsync runs EnsureDefaultAsync inside its own
        // transaction on this very context, and opening a second one throws on MariaDB. Whoever
        // opened commits, and an exception rolls the whole of it back.
        if (context.Database.CurrentTransaction is not null)
        {
            var nested = await body();

            // The commit rule cannot hold here: the ambient transaction is not ours to withhold.
            // Rather than commit a refusal in silence, refuse to be the trap — no reentrant caller
            // answers a Result today, and the one that tries will say so instead of drifting.
            if (nested is CSharpFunctionalExtensions.IResult { IsFailure: true })
            {
                throw new InvalidOperationException(
                    "A reentrant CalendarStore call answered a failed Result; the ambient transaction cannot roll it back.");
            }

            return nested;
        }

        Func<CancellationToken, Task<T>> operation = async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            var outcome = await body();
            if (outcome is CSharpFunctionalExtensions.IResult { IsFailure: true }) return outcome;

            await transaction.CommitAsync(token);
            return outcome;
        };
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(operation, cancellationToken);
    }
}
