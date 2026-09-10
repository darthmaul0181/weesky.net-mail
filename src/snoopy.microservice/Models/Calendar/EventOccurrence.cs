namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>
/// One instance of an event inside a window, in the shape its own time has: a dated one carries
/// <c>StartUtc</c>/<c>EndUtc</c> and the IANA zone they were read in, an all-day one carries the
/// dates the file wrote — <c>EndDateExclusive</c> is the morning after — and a floating one carries
/// wall-clock readings of kind <see cref="DateTimeKind.Unspecified"/>, which belong to no zone.
///
/// <c>InstanceId</c> is the RECURRENCE-ID a client would have to write to address this instance —
/// the literal value in the master's DTSTART form, never the UTC instant, and "" for an event that
/// does not repeat. <c>RecurrenceText</c> is the master's RRULE value, the same for every instance.
///
/// <c>EventId</c> and <c>CalendarId</c> are the row the instance came from: a window spans every
/// calendar of one user at once, and the client filters and colours by calendar without a second
/// read. The engine carries them through rather than knowing them — the store hands them in.
/// </summary>
public sealed record EventOccurrence(
    Guid EventId,
    Guid CalendarId,
    string Uid,
    string InstanceId,
    bool IsOverride,
    bool IsAllDay,
    bool IsFloating,
    string? TimeZone,
    DateTime? StartUtc,
    DateTime? EndUtc,
    DateOnly? StartDate,
    DateOnly? EndDateExclusive,
    DateTime? LocalStart,
    DateTime? LocalEnd,
    string? Summary,
    string? Location,
    string? Status,
    string Transparency,
    string? Class,
    bool HasAlarm,
    string? RecurrenceText);
