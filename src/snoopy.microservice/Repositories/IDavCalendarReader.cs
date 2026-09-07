using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Calendar;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// <see cref="IDavContactReader"/>'s twin, keyed by calendar rather than by user: CalDAV syncs each
/// collection on its own, so every read but the listing takes a calendar id. A calendar of another
/// user and an event of another calendar both answer nothing — the same 404, never a 403 that would
/// confirm the resource exists. Public because the controller, which can only be public, injects it.
/// </summary>
public interface IDavCalendarReader
{
    /// <summary>Every calendar of one user, hidden ones included — <c>is_visible</c> is a sidebar
    /// checkbox and never a projection (cadrage, décision 2) — in <c>sort_order</c> then name.</summary>
    Task<IReadOnlyList<DavCalendar>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<DavCalendar?> FindCalendarAsync(Guid userId, string calendarName, CancellationToken cancellationToken);

    /// <summary><c>upTo</c> is the counter the answer's ctag was cut from: a member above it would
    /// be covered by a ctag the list does not carry.</summary>
    IAsyncEnumerable<DavEvent> StreamAsync(Guid calendarId, ulong upTo, CancellationToken cancellationToken);

    Task<DavEvent?> FindAsync(Guid calendarId, string davName, CancellationToken cancellationToken);

    Task<IReadOnlyList<DavEvent>> FindManyAsync(
        Guid calendarId, IReadOnlyList<string> davNames, CancellationToken cancellationToken);

    IAsyncEnumerable<DavEvent> ChangedAsync(
        Guid calendarId, ulong after, ulong upTo, CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendarTombstone>> TombstonesAsync(
        Guid calendarId, ulong after, ulong upTo, CancellationToken cancellationToken);

    /// <summary>
    /// The rows a <c>calendar-query</c> or a <c>free-busy-query</c> may match, on the columns alone:
    /// a null bound is open, and the window carries the day of slack
    /// <see cref="CalendarEventStore"/> already applies. Each candidate is then judged on its file.
    /// </summary>
    IAsyncEnumerable<DavEvent> CandidatesAsync(Guid calendarId, DateTime? fromUtc, DateTime? toUtc,
        EventColumnFilter columns, ulong upTo, CancellationToken cancellationToken);

    /// <summary>What the collection holds — the ceiling a PUT counts inside its gate.</summary>
    Task<int> CountAsync(Guid calendarId, CancellationToken cancellationToken);
}
