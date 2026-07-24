using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One curated sending identity. Addresses are stored canonical (trimmed, lower-case): the
/// table collates in binary, so a casing difference would split one identity into two.
/// </summary>
[Table("sending_identities")]
public sealed class SendingIdentity
{
    [Column("account_id")]
    public string AccountId { get; set; } = string.Empty;

    [Column("address")]
    public string Address { get; set; } = string.Empty;

    [Column("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [Column("is_default")]
    public bool IsDefault { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
