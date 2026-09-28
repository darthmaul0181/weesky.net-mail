namespace weesky.Scotty.Microservice.Models.Calendar;

/// <summary>The body of PUT /api/Calendars/Birthdays: the birthdays calendar's switch.</summary>
public sealed class BirthdaysToggleRequest
{
    public bool Enabled { get; set; }
}
