using CSharpFunctionalExtensions;
using weesky.Scotty.Microservice.Models.Calendar;

namespace weesky.Scotty.Microservice.Repositories;

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
    /// The birthdays collection, created and filled in one transaction when the user has none and
    /// <c>calendar.birthdays</c> is on (spec, décision 3). Its language is <c>ui.language</c> when that
    /// is <c>fr</c> or <c>en</c>, else <paramref name="language"/>, else English. Idempotent, and two
    /// cheap reads when there is nothing to do: it runs on every calendar list.
    /// </summary>
    Task EnsureBirthdaysAsync(
        Guid userId, string browserTimeZone, string language, CancellationToken cancellationToken);

    /// <summary>The switch: writes <c>calendar.birthdays</c>, then ensures the collection or removes
    /// it — events, state and tombstones with it, and no revision, since it holds only copies.</summary>
    Task SetBirthdaysEnabledAsync(
        Guid userId, bool enabled, string browserTimeZone, string language, CancellationToken cancellationToken);

    /// <summary>Rewrites every birthday in <paramref name="language"/> under one rank; nothing at all
    /// when the collection is missing or already speaks it.</summary>
    Task SetBirthdayLanguageAsync(Guid userId, string language, CancellationToken cancellationToken);

    /// <summary>
    /// A new collection: its <c>dav_name</c> is its id, its colour the palette's next, its rank the
    /// last. Refused past <see cref="CalendarStore.MaxPerUser"/>.
    /// </summary>
    Task<Result<Guid>> CreateAsync(
        Guid userId, CalendarWrite write, string browserTimeZone, CancellationToken cancellationToken);

    /// <summary>
    /// The same collection created from a DAV client: décision 2 of the overview makes the URL
    /// segment the client chose its <c>dav_name</c>. Colour next of the palette, rank last, the
    /// state row in the same transaction. When <c>write.TimeZone</c> is null: the zone of
    /// <c>default</c>, and UTC when the account holds no calendar at all — a hand-restored base
    /// (§ 6), where a MKCALENDAR carries no browser to ask.
    /// Refused with <see cref="CalendarStore.CapReached"/> or <see cref="CalendarStore.NameTaken"/>.
    /// </summary>
    Task<Result<Guid>> CreateNamedAsync(
        Guid userId, string davName, CalendarWrite write, CancellationToken cancellationToken);

    /// <summary>
    /// The name, the description, the colour, the rank and — from a DAV client alone — the zone,
    /// which the caller has already resolved to an IANA id. Advances neither ctag nor sequence:
    /// none of them is an event, and waking every phone for a colour is one sync per rename. The
    /// birthdays collection's reminder is the exception: a new one rewrites every event under one rank.
    /// </summary>
    Task<Result> UpdateAsync(
        Guid userId, Guid calendarId, CalendarWrite write, CancellationToken cancellationToken);

    /// <summary>The sidebar checkbox, and nothing else — never projected to DAV (décision 2).</summary>
    Task<Result> SetVisibleAsync(
        Guid userId, Guid calendarId, bool visible, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the collection, archiving every event it held in batches of
    /// <see cref="CalendarStore.DeleteBatch"/> and taking its sync state and its tombstones with
    /// it. The <c>default</c> collection is refused: a user with no calendar has nowhere to write. So
    /// is the birthdays one, which only its switch removes.
    /// </summary>
    Task<Result> DeleteAsync(Guid userId, Guid calendarId, CancellationToken cancellationToken);
}
