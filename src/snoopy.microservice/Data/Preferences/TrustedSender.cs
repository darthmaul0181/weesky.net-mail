using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One sender whose remote images this account loads without asking. Addresses are stored
/// canonical (trimmed, lower-case): the table collates in binary, so a casing difference would
/// split one sender into two rows and the second would silently never match.
/// </summary>
[Table("trusted_senders")]
public sealed class TrustedSender
{
    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("address")]
    public string Address { get; set; } = string.Empty;

    [Column("last_used")]
    public DateTime LastUsed { get; set; }
}
