using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One postal address of one contact. <see cref="Position"/> is the rank of the ADR property on
/// the card, the composer's handle — display order comes from <c>(Pref, Position)</c> instead.
/// </summary>
[Table("contact_addresses")]
public sealed class ContactAddress
{
    [Column("contact_id")]
    public Guid ContactId { get; set; }

    [Column("position")]
    public int Position { get; set; }

    [Column("type")]
    public string Type { get; set; } = string.Empty;

    [Column("pref")]
    public int Pref { get; set; } = 101;

    /// <summary>Verbatim, LABEL included — the formatted 4.0 address can be long.</summary>
    [Column("params")]
    public string Params { get; set; } = string.Empty;

    [Column("group_name")]
    public string GroupName { get; set; } = string.Empty;

    [Column("po_box")]
    public string? PoBox { get; set; }

    [Column("extended")]
    public string? Extended { get; set; }

    [Column("street")]
    public string? Street { get; set; }

    [Column("locality")]
    public string? Locality { get; set; }

    [Column("region")]
    public string? Region { get; set; }

    [Column("postal_code")]
    public string? PostalCode { get; set; }

    [Column("country")]
    public string? Country { get; set; }
}
