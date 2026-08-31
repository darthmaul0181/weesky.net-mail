using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One address of one contact. Stored canonical (trimmed, lower-case): the table collates in
/// binary, so a casing difference would split one address into two rows. <see cref="Position"/>
/// is the rank of the EMAIL property on the card, the composer's handle — display order comes
/// from <c>(Pref, Position)</c> instead.
/// </summary>
[Table("contact_emails")]
public sealed class ContactEmail
{
    [Column("contact_id")]
    public Guid ContactId { get; set; }

    [Column("address")]
    public string Address { get; set; } = string.Empty;

    [Column("position")]
    public int Position { get; set; }

    /// <summary>TYPE extracted from Params, for display; empty = untyped.</summary>
    [Column("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>Normalised PREF (1..100); 101 = the card says nothing. Sort: (Pref, Position).</summary>
    [Column("pref")]
    public int Pref { get; set; } = 101;

    /// <summary>Verbatim parameter block (TYPE=WORK;PREF=1); display only, never re-emitted.</summary>
    [Column("params")]
    public string Params { get; set; } = string.Empty;

    /// <summary>Property group (item1); what ties in an Apple X-ABLabel.</summary>
    [Column("group_name")]
    public string GroupName { get; set; } = string.Empty;
}
