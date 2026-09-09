using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar;
using CalendarRow = weesky.Snoopy.Microservice.Data.Preferences.Calendar;

namespace weesky.Snoopy.Microservice.Repositories;

/// <inheritdoc cref="IDavCalendarReader"/>
internal sealed class DavCalendarReader(PreferencesDbContext context) : IDavCalendarReader
{
    /// <summary>
    /// The preselection's widening on both sides of a report's window. The columns hold instants
    /// posed in the collection's zone; a calendar-query judges an all-day or floating instance in
    /// the REQUEST's (RFC 4791 § 9.8), and the two may lie 26 hours apart — UTC+14 against UTC−12.
    /// Its own constant, not <see cref="OccurrenceExpander.Margin"/>: that one is the walk's, and
    /// its equality with the store's window query is an invariant this band has no part in.
    /// </summary>
    internal static readonly TimeSpan Slack = TimeSpan.FromHours(26);

    private static readonly Expression<Func<CalendarRow, DavCalendar>> ToCalendar = c =>
        new DavCalendar(c.Id, c.UserId, c.DavName, c.DisplayName, c.Description, c.Color, c.Order,
            c.TimeZone);

    private static readonly Expression<Func<CalendarEvent, DavEvent>> ToEvent = e =>
        new DavEvent(e.Id, e.CalendarId, e.DavName, e.Uid, e.IcsRaw, e.IcsHash, e.UpdatedAt,
            e.SyncSequence);

    public async Task<IReadOnlyList<DavCalendar>> ListAsync(
        Guid userId, CancellationToken cancellationToken) =>
        await context.Calendars.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Order).ThenBy(c => c.DavName)
            .Select(ToCalendar)
            .ToListAsync(cancellationToken);

    public async Task<DavCalendar?> FindCalendarAsync(
        Guid userId, string calendarName, CancellationToken cancellationToken) =>
        await context.Calendars.AsNoTracking()
            .Where(c => c.UserId == userId && c.DavName == calendarName)
            .Select(ToCalendar)
            .SingleOrDefaultAsync(cancellationToken);

    public async IAsyncEnumerable<DavEvent> StreamAsync(
        Guid calendarId, ulong upTo, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var events = Members(calendarId)
            .Where(e => e.SyncSequence <= upTo)
            .Select(ToEvent)
            .AsAsyncEnumerable();
        await foreach (var member in events.WithCancellation(cancellationToken))
        {
            yield return member;
        }
    }

    public async Task<DavEvent?> FindAsync(
        Guid calendarId, string davName, CancellationToken cancellationToken) =>
        await Members(calendarId)
            .Where(e => e.DavName == davName)
            .Select(ToEvent)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<DavEvent>> FindManyAsync(
        Guid calendarId, IReadOnlyList<string> davNames, CancellationToken cancellationToken) =>
        await Members(calendarId)
            .Where(e => davNames.Contains(e.DavName))
            .Select(ToEvent)
            .ToListAsync(cancellationToken);

    public async IAsyncEnumerable<DavEvent> ChangedAsync(Guid calendarId, ulong after, ulong upTo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Ordered by rank: the order is what makes a truncation able to cut on a rank boundary, and
        // no small-volume test would catch losing it.
        var events = Members(calendarId)
            .Where(e => e.SyncSequence > after && e.SyncSequence <= upTo)
            .OrderBy(e => e.SyncSequence)
            .Select(ToEvent)
            .AsAsyncEnumerable();
        await foreach (var member in events.WithCancellation(cancellationToken))
        {
            yield return member;
        }
    }

    public async Task<IReadOnlyList<CalendarTombstone>> TombstonesAsync(
        Guid calendarId, ulong after, ulong upTo, CancellationToken cancellationToken) =>
        await context.CalendarTombstones
            .Where(t => t.CalendarId == calendarId && t.SyncSequence > after && t.SyncSequence <= upTo)
            .OrderBy(t => t.SyncSequence)
            .ToListAsync(cancellationToken);

    public async IAsyncEnumerable<DavEvent> CandidatesAsync(Guid calendarId, DateTime? fromUtc,
        DateTime? toUtc, EventColumnFilter columns, ulong upTo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = Members(calendarId).Where(e => e.SyncSequence <= upTo);

        // Wider than the columns by Slack: they hold instants posed in the calendar's zone while
        // the expander judges in the request's, so bare bounds would drop a candidate before
        // anything has judged it — a calendar-query answering false with nothing in the logs.
        if (toUtc is { } to)
        {
            var upper = CalendarEventStore.Shift(to, Slack);
            query = query.Where(e => e.FirstOccurrence < upper);
        }

        if (fromUtc is { } from)
        {
            var lower = CalendarEventStore.Shift(from, -Slack);
            query = query.Where(e => e.LastOccurrence > lower);
        }

        // The columns hold the master alone, and a prop-filter is satisfied by ANY component of the
        // resource: a recurring row — the one shape that carries overrides — is never preselected
        // on them, or an override matching on its own would be dropped before the file was read.
        if (columns.Status is { } status) query = query.Where(e => e.IsRecurring || e.Status == status);
        if (columns.Transparency is { } transparency)
            query = query.Where(e => e.IsRecurring || e.Transparency == transparency);
        if (columns.Class is { } classification)
            query = query.Where(e => e.IsRecurring || e.Class == classification);

        var events = query.OrderBy(e => e.DavName).Select(ToEvent).AsAsyncEnumerable();
        await foreach (var member in events.WithCancellation(cancellationToken))
        {
            yield return member;
        }
    }

    public async Task<int> CountAsync(Guid calendarId, CancellationToken cancellationToken) =>
        await Members(calendarId).CountAsync(cancellationToken);

    private IQueryable<CalendarEvent> Members(Guid calendarId) =>
        context.CalendarEvents.AsNoTracking().Where(e => e.CalendarId == calendarId);
}
