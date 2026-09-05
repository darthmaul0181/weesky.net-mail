namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>
/// The body of POST /api/Calendar/Events, and the base of <see cref="EventUpdateRequest"/>.
/// Settable, bound from JSON; every field is optional at the wire level so
/// <see cref="EventRequestValidator"/> can answer one clear message instead of the binder answering
/// several unclear ones.
/// </summary>
public class EventRequest
{
    public Guid CalendarId { get; set; }

    public string? Summary { get; set; }

    public string? Location { get; set; }

    public string? Description { get; set; }

    public bool IsAllDay { get; set; }

    /// <summary>Wall-clock reading in <see cref="TimeZone"/>; ignored for an all-day event.</summary>
    public DateTime? Start { get; set; }

    public DateTime? End { get; set; }

    public string? TimeZone { get; set; }

    /// <summary>All-day only.</summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>All-day only, inclusive — the last day shown. Named to match
    /// <see cref="EventWrite.EndDateInclusive"/>, which is what a save reads back as.</summary>
    public DateOnly? EndDateInclusive { get; set; }

    public RecurrenceRequest? Repeat { get; set; }

    public List<int>? ReminderMinutesBefore { get; set; }

    public Availability Availability { get; set; } = Availability.Busy;

    public Visibility Visibility { get; set; } = Visibility.Default;

    public string? Url { get; set; }
}
