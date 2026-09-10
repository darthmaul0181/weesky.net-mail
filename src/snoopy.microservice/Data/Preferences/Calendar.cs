using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One calendar collection of one webmail user — a CalDAV collection, and the unit every event,
/// tombstone and sync counter hangs from. Flat like its Contacts siblings: no navigation property.
/// </summary>
[Table("calendars")]
public sealed class Calendar
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    /// <summary>
    /// Last segment of the CalDAV URL, fixed at creation and never renamed: a client syncs on it,
    /// and moving it makes every event it holds a new resource.
    /// </summary>
    [Column("dav_name")]
    public string DavName { get; set; } = string.Empty;

    [Column("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [Column("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>#RRGGBB; Apple's alpha channel is stripped on write.</summary>
    [Column("color")]
    public string Color { get; set; } = string.Empty;

    /// <summary>
    /// Sidebar rank. The column is <c>sort_order</c> because ORDER is an SQL keyword, and a column
    /// that only exists between back-quotes is a production error waiting in a project where the
    /// SQL is replayed by hand.
    /// </summary>
    [Column("sort_order")]
    public int Order { get; set; }

    /// <summary>IANA id — the browser's own at creation (décision 6); never a Windows one.</summary>
    [Column("time_zone")]
    public string TimeZone { get; set; } = string.Empty;

    /// <summary>The sidebar checkbox. A local display state, never projected to DAV (décision 2).</summary>
    [Column("is_visible")]
    public bool IsVisible { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
