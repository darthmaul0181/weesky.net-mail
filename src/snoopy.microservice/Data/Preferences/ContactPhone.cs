using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One phone number of one contact. <see cref="Position"/> is the rank of the TEL property on the
/// card, the composer's handle — display order comes from <c>(Pref, Position)</c> instead.
/// </summary>
[Table("contact_phones")]
public sealed class ContactPhone
{
    [Column("contact_id")]
    public Guid ContactId { get; set; }

    [Column("position")]
    public int Position { get; set; }

    /// <summary>As carried by the card; no canonicalisation.</summary>
    [Column("number")]
    public string Number { get; set; } = string.Empty;

    [Column("type")]
    public string Type { get; set; } = string.Empty;

    [Column("pref")]
    public int Pref { get; set; } = 101;

    [Column("params")]
    public string Params { get; set; } = string.Empty;

    [Column("group_name")]
    public string GroupName { get; set; } = string.Empty;
}
