using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One contact of one webmail user. Flat like its sibling entities — no navigation property to
/// the addresses: the store joins them, which keeps every read one explicit query.
/// </summary>
[Table("contacts")]
public sealed class Contact
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    /// <summary>
    /// The source vCard's own UID, kept distinct from <see cref="Id"/>: a client that PUTs UID X
    /// and reads back UID Y sees a different card and duplicates it on the next sync. Set to
    /// <see cref="Id"/> for a contact born here.
    /// </summary>
    [Column("uid")]
    public string Uid { get; set; } = string.Empty;

    [Column("first_name")]
    public string? FirstName { get; set; }

    [Column("last_name")]
    public string? LastName { get; set; }

    [Column("nickname")]
    public string? Nickname { get; set; }

    [Column("is_favorite")]
    public bool IsFavorite { get; set; }

    /// <summary>
    /// Where the card came from. Written at creation and never afterwards: editing a captured
    /// contact must not reclassify it as one somebody typed.
    /// </summary>
    [Column("source")]
    public string Source { get; set; } = "manual";

    /// <summary>
    /// The source vCard verbatim, written by the import path only and never served to the UI. It
    /// is what stops a property we do not model from being destroyed on a future CardDAV sync.
    /// </summary>
    [Column("vcard_raw")]
    public string? VCardRaw { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
