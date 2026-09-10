namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>The body of POST /api/Calendars and PUT /api/Calendars/{id}. Settable, bound from
/// JSON; a null <see cref="Color"/>/<see cref="Order"/> on an update keeps the stored value.</summary>
public sealed class CalendarRequest
{
    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public string? Color { get; set; }

    public int? Order { get; set; }
}
