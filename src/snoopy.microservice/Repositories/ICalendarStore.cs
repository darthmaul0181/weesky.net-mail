using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Calendar;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// The collections themselves, the unit every event, tombstone and sync counter hangs from. Its
/// twin for the resources is <see cref="ICalendarEventStore"/>; both share
/// <see cref="ICalendarSyncStore"/>, which is what makes their ranks one sequence per calendar.
/// </summary>
public interface ICalendarStore
{
    /// <summary>Every calendar of one user, hidden ones included: the checkbox is a display state,
    /// so a caller that filtered them out here could never offer to tick one back on.</summary>
    Task<IReadOnlyList<CalendarView>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// The <c>default</c> collection, created with <paramref name="browserTimeZone"/> when the user
    /// has none (décision 6). Idempotent: a second call answers the first one's calendar, zone
    /// included — the browser's zone decides once, when the account is first opened.
    /// </summary>
    Task<CalendarView> EnsureDefaultAsync(
        Guid userId, string browserTimeZone, CancellationToken cancellationToken);

    /// <summary>
    /// A new collection: its <c>dav_name</c> is its id, its colour the palette's next, its rank the
    /// last. Refused past <see cref="CalendarStore.MaxPerUser"/>.
    /// </summary>
    Task<Result<Guid>> CreateAsync(
        Guid userId, CalendarWrite write, string browserTimeZone, CancellationToken cancellationToken);

    /// <summary>
    /// The name, the description, the colour and the rank. Advances neither ctag nor sequence: none
    /// of them is an event, and waking every phone for a colour is one sync per rename.
    /// </summary>
    Task<Result> UpdateAsync(
        Guid userId, Guid calendarId, CalendarWrite write, CancellationToken cancellationToken);

    /// <summary>The sidebar checkbox, and nothing else — never projected to DAV (décision 2).</summary>
    Task<Result> SetVisibleAsync(
        Guid userId, Guid calendarId, bool visible, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the collection, archiving every event it held in batches of
    /// <see cref="CalendarStore.DeleteBatch"/> and taking its sync state and its tombstones with
    /// it. The <c>default</c> collection is refused: a user with no calendar has nowhere to write.
    /// </summary>
    Task<Result> DeleteAsync(Guid userId, Guid calendarId, CancellationToken cancellationToken);
}
