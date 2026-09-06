namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>
/// One stored resource as the editor opens it. <c>Fields</c> is the resource read back as an
/// <see cref="EventWrite"/> — the inverse of the composer — so that saving without touching
/// anything writes the same event. <c>IcsHash</c> is what the save sends back to prove it edited
/// this version and not one somebody replaced meanwhile. <c>RepeatIsExact</c> and
/// <c>ForeignAlarms</c> are what the editor cannot show and must therefore warn about: a rule its
/// own controls would narrow, and the alarms its bell does not carry.
/// </summary>
public sealed record EventDetail(
    Guid Id, Guid CalendarId, string Uid, string IcsHash, EventWrite Fields, string? RecurrenceText,
    IReadOnlyList<AttendeeProjection> Attendees, string? Status, bool RepeatIsExact,
    IReadOnlyList<string> ForeignAlarms);
