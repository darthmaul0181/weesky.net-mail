using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// A name that disappeared, and the rank at which it did. A state and not a journal: one row per
/// name, the newest overwriting the previous, because a client of sync-collection never asks for
/// the path travelled — only for the state on arrival.
/// </summary>
[Table("contact_tombstones")]
public sealed class ContactTombstone
{
    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("dav_name")]
    public string DavName { get; set; } = string.Empty;

    [Column("sync_sequence")]
    public ulong SyncSequence { get; set; }

    [Column("deleted_at")]
    public DateTime DeletedAt { get; set; }
}
