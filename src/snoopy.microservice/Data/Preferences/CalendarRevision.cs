using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// The bytes a write replaced or a deletion removed. Bytes and not a diff: ics_raw is already the
/// sovereign data, and a revision that had to be replayed to be read would not be a backup.
/// </summary>
[Table("calendar_revisions")]
public sealed class CalendarRevision
{
    [Column("id")]
    public ulong Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    /// <summary>The calendar while it still exists; the archive outlives it (décision 2).</summary>
    [Column("calendar_id")]
    public Guid? CalendarId { get; set; }

    /// <summary>The event while it still exists; a <c>Delete</c> revision outlives its own.</summary>
    [Column("event_id")]
    public Guid? EventId { get; set; }

    /// <summary>
    /// The archived resource's UID, the identity arbiter. Null only when a refused body does not
    /// parse at all, on the table whose whole job is to lose nothing.
    /// </summary>
    [Column("uid")]
    public string? Uid { get; set; }

    [Column("dav_name")]
    public string? DavName { get; set; }

    [Column("ics_hash")]
    public string IcsHash { get; set; } = string.Empty;

    [Column("ics_raw")]
    public string IcsRaw { get; set; } = string.Empty;

    [Column("cause")]
    public RevisionCause Cause { get; set; }

    [Column("replaced_at")]
    public DateTime ReplacedAt { get; set; }
}
