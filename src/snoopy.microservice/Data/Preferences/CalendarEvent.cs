using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One CalDAV resource: the whole VCALENDAR in <see cref="IcsRaw"/>, sovereign, and the columns
/// around it an index over it — never a second source of truth.
/// </summary>
[Table("calendar_events")]
public sealed class CalendarEvent
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("calendar_id")]
    public Guid CalendarId { get; set; }

    /// <summary>
    /// Redundant with <see cref="Calendar.UserId"/>: the API's window query asks every calendar of
    /// one user at once, and joining to find the owner would cost a scan per read.
    /// </summary>
    [Column("user_id")]
    public Guid UserId { get; set; }

    /// <summary>Unique per calendar, not per user (RFC 4791 § 4.1).</summary>
    [Column("uid")]
    public string Uid { get; set; } = string.Empty;

    [Column("dav_name")]
    public string DavName { get; set; } = string.Empty;

    [Column("summary")]
    public string? Summary { get; set; }

    [Column("location")]
    public string? Location { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    /// <summary>UTC; a date without a time, or a floating one, is placed in the calendar's zone.</summary>
    [Column("starts_at")]
    public DateTime StartsAt { get; set; }

    [Column("ends_at")]
    public DateTime EndsAt { get; set; }

    [Column("is_all_day")]
    public bool IsAllDay { get; set; }

    /// <summary>IANA id, "UTC", or null = floating (décision 5).</summary>
    [Column("time_zone")]
    public string? TimeZone { get; set; }

    [Column("is_recurring")]
    public bool IsRecurring { get; set; }

    [Column("first_occurrence")]
    public DateTime FirstOccurrence { get; set; }

    /// <summary>2100-01-01 for an endless rule (décision 1), so the window query stays one range scan.</summary>
    [Column("last_occurrence")]
    public DateTime LastOccurrence { get; set; }

    [Column("status")]
    public string? Status { get; set; }

    [Column("transparency")]
    public string Transparency { get; set; } = "OPAQUE";

    [Column("class")]
    public string? Class { get; set; }

    [Column("ics_raw")]
    public string IcsRaw { get; set; } = string.Empty;

    /// <summary>SHA-256 hex of <see cref="IcsRaw"/> — base of the CalDAV ETag. "" = not computed yet.</summary>
    [Column("ics_hash")]
    public string IcsHash { get; set; } = string.Empty;

    /// <summary>
    /// Rank of the last write that changed this resource. Zero means never ranked, and zero is the
    /// value a sync token never asks for — such a row is invisible to the protocol.
    /// </summary>
    [Column("sync_sequence")]
    public ulong SyncSequence { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
