namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>
/// What the editor sends, in the two shapes an event has. A dated one carries <c>Start</c> and
/// <c>End</c> as wall-clock readings (kind Unspecified) in <c>TimeZone</c>, the browser's IANA id;
/// an all-day one carries the dates, <c>EndDateInclusive</c> being the last day shown — the
/// composer writes the exclusive DTEND RFC 5545 wants (décision 5).
/// </summary>
public sealed record EventWrite(
    Guid CalendarId,
    string? Summary,
    string? Location,
    string? Description,
    bool IsAllDay,
    DateTime? Start,
    DateTime? End,
    string? TimeZone,
    DateOnly? StartDate,
    DateOnly? EndDateInclusive,
    RecurrenceWrite? Repeat,
    IReadOnlyList<int> ReminderMinutesBefore,
    Availability Availability,
    Visibility Visibility,
    string? Url);
