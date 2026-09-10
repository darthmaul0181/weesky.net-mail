namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>The body of PUT /api/Calendars/{id}/Visible.</summary>
public sealed class CalendarVisibleRequest
{
    public bool Visible { get; set; }
}
