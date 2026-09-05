using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One ATTENDEE or ORGANIZER projected out of an event's card, for the reads that ask who is on an
/// event without parsing every ICS. Keyed on the rank, not the address: an override component may
/// legally repeat the same person, and the card stays the source of truth either way.
/// </summary>
[Table("calendar_attendees")]
public sealed class CalendarAttendee
{
    [Column("event_id")]
    public Guid EventId { get; set; }

    [Column("position")]
    public int Position { get; set; }

    /// <summary>Literal RECURRENCE-ID of the component it came from; null = the master.</summary>
    [Column("recurrence_id")]
    public string? RecurrenceId { get; set; }

    [Column("email")]
    public string Email { get; set; } = string.Empty;

    [Column("name")]
    public string? Name { get; set; }

    [Column("role")]
    public string? Role { get; set; }

    [Column("partstat")]
    public string? PartStat { get; set; }

    [Column("is_organizer")]
    public bool IsOrganizer { get; set; }
}
