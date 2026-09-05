namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>
/// The index a CalDAV resource yields: what the columns of <c>calendar_events</c> hold, in the
/// units they hold it — every instant UTC, every zone an IANA id.
///
/// <c>TimeZone</c> is an IANA id, "UTC", or null for a floating or all-day event.
/// <c>LastOccurrence</c> is <see cref="Services.Calendar.IcsProjector.NoEnd"/> when the rule never
/// ends, or when its zone is one no calendar database knows and the expansion could not run.
/// <c>UnknownTimeZone</c> says the component named such a TZID: the instants are still right — the
/// file's own VTIMEZONE, else the calendar's zone — but the store is the layer that logs it.
/// </summary>
internal sealed record EventProjection(
    string Uid, string? Summary, string? Location, string? Description,
    DateTime StartsAt, DateTime EndsAt, bool IsAllDay, string? TimeZone,
    bool IsRecurring, DateTime FirstOccurrence, DateTime LastOccurrence,
    string? Status, string Transparency, string? Class,
    IReadOnlyList<AttendeeProjection> Attendees, bool UnknownTimeZone = false);
