using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// The bytes a write replaced or a deletion removed, kept for thirty days. Bytes and not a diff:
/// vcard_raw is already the sovereign data, and a revision that had to be replayed to be read
/// would not be a backup.
/// </summary>
[Table("contact_revisions")]
public sealed class ContactRevision
{
    [Column("id")]
    public ulong Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    /// <summary>The contact while it still exists; a <c>Delete</c> revision outlives its own.</summary>
    [Column("contact_id")]
    public Guid? ContactId { get; set; }

    /// <summary>
    /// The archived card's UID, the identity arbiter. Null only when a refused body does not parse
    /// into a card at all, on the table whose whole job is to lose nothing.
    /// </summary>
    [Column("uid")]
    public string? Uid { get; set; }

    [Column("dav_name")]
    public string? DavName { get; set; }

    [Column("card_hash")]
    public string CardHash { get; set; } = string.Empty;

    [Column("vcard_raw")]
    public string VCardRaw { get; set; } = string.Empty;

    [Column("cause")]
    public RevisionCause Cause { get; set; }

    [Column("replaced_at")]
    public DateTime ReplacedAt { get; set; }
}
