namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>
/// One ORGANIZER or ATTENDEE line of one component, kept with the RECURRENCE-ID of the component
/// it was written on — null for the master. The same person legitimately appears twice.
/// </summary>
public sealed record AttendeeProjection(
    string? RecurrenceId, string Email, string? Name, string? Role, string? PartStat, bool IsOrganizer);
