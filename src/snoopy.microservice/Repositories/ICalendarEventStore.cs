using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Calendar;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// The resources: one row per UID per calendar, its whole VCALENDAR sovereign in <c>ics_raw</c> and
/// the columns around it an index over it. Every write takes its rank from
/// <see cref="ICalendarSyncStore.NextSequenceAsync"/> first, and nothing here hashes on its own.
/// </summary>
public interface ICalendarEventStore
{
    /// <summary>
    /// Every instance falling in <c>[fromUtc, toUtc[</c>, across every calendar of the user —
    /// hidden ones included, since each occurrence carries its own <c>CalendarId</c> and the client
    /// is what filters. <paramref name="viewTimeZone"/> only decides which day a floating instance
    /// falls on; a dated one is already an instant. A window holding more than
    /// <see cref="CalendarEventStore.MaxWindowOccurrences"/> instances is refused, never truncated.
    /// </summary>
    Task<Result<IReadOnlyList<EventOccurrence>>> WindowAsync(
        Guid userId, DateTime fromUtc, DateTime toUtc, string viewTimeZone,
        CancellationToken cancellationToken);

    /// <summary>One resource as the editor opens it, or null — a resource of another user is
    /// indistinguishable from one that does not exist.</summary>
    Task<EventDetail?> GetAsync(Guid userId, Guid eventId, CancellationToken cancellationToken);

    Task<Result<Guid>> CreateAsync(Guid userId, EventWrite write, CancellationToken cancellationToken);

    /// <summary>
    /// <paramref name="ifHash"/>, when given, must still be the resource's <c>ics_hash</c> or the
    /// write is refused as <see cref="CalendarEventStore.EventMoved"/>.
    /// <paramref name="instanceId"/> names the occurrence for the two narrow scopes, spelled in the
    /// master's own DTSTART form.
    /// </summary>
    Task<Result> UpdateAsync(
        Guid userId, Guid eventId, EditScope scope, string? instanceId, EventWrite write,
        string? ifHash, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(
        Guid userId, Guid eventId, EditScope scope, string? instanceId,
        CancellationToken cancellationToken);

    /// <summary>Fonctionnalité 5: one result per event, at the occurrence that comes next — or the
    /// last one it ever had, for a series already over.</summary>
    Task<IReadOnlyList<EventOccurrence>> SearchAsync(
        Guid userId, string text, CancellationToken cancellationToken);

    /// <summary>
    /// One file in, grouped by UID into resources (fonctionnalité 6). An existing UID is replaced
    /// whole and its previous bytes archived; VTODO and VJOURNAL are counted, never stored.
    /// </summary>
    Task<CalendarImportOutcome> ImportAsync(
        Guid userId, Guid calendarId, string vcalendar, CancellationToken cancellationToken);

    /// <summary>One VCALENDAR carrying every resource of the collection, its VTIMEZONEs written
    /// once each, and the collection's own name and colour.</summary>
    Task<string> ExportAsync(Guid userId, Guid calendarId, CancellationToken cancellationToken);
}
