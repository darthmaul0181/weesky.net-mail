namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>GET /api/Calendar/Events/{id} — <see cref="EventDetail"/>'s fields flattened onto one
/// response, so the client reads one shape instead of an internal store type.</summary>
public sealed record EventResponse(
    Guid Id, Guid CalendarId, string Uid, string IcsHash, EventWrite Fields, string? RecurrenceText,
    IReadOnlyList<AttendeeProjection> Attendees, string? Status)
{
    internal static EventResponse From(EventDetail detail) =>
        new(detail.Id, detail.CalendarId, detail.Uid, detail.IcsHash, detail.Fields,
            detail.RecurrenceText, detail.Attendees, detail.Status);
}
