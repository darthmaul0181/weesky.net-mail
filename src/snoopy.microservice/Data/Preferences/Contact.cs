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

    /// <summary>
    /// The card's FN, stored because the frontend otherwise guesses it (first + last, else
    /// nickname, else first address). A card carrying "Dr. John Smith Jr." must not be flattened.
    /// </summary>
    [Column("display_name")]
    public string? DisplayName { get; set; }

    [Column("middle_name")]
    public string? MiddleName { get; set; }

    [Column("name_prefix")]
    public string? NamePrefix { get; set; }

    [Column("name_suffix")]
    public string? NameSuffix { get; set; }

    [Column("organization")]
    public string? Organization { get; set; }

    /// <summary>ORG components 2..n, joined by ';' as they appear on the card.</summary>
    [Column("department")]
    public string? Department { get; set; }

    [Column("job_title")]
    public string? JobTitle { get; set; }

    /// <summary>
    /// vCard form verbatim: a partial date (--0315) or free text is valid. Interpretation is a
    /// display concern, left to 4b.
    /// </summary>
    [Column("birthday")]
    public string? Birthday { get; set; }

    /// <summary>First occurrence of URL; the following ones stay in the card.</summary>
    [Column("website")]
    public string? Website { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }

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

    /// <summary>SHA-256 hex of <see cref="VCardRaw"/>; base of the CardDAV ETag. "" = not computed yet.</summary>
    [Column("card_hash")]
    public string CardHash { get; set; } = string.Empty;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
