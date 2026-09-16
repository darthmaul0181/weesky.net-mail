namespace weesky.Scotty.Microservice.Models.Calendar;

/// <summary>GET /api/Calendars.</summary>
public sealed record CalendarListResponse(IReadOnlyList<CalendarView> Calendars);
