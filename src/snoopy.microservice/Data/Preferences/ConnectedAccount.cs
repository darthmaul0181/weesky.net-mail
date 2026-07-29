using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One extra mailbox a user attached to their session — an external provider's when
/// <see cref="DomainId"/> names one, a local shared mailbox when it is null. The id is the
/// value the client sends back to select the account.
/// </summary>
[Table("connected_accounts")]
public sealed class ConnectedAccount
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    /// <summary>Null = the home server (a local shared mailbox).</summary>
    [Column("domain_id")]
    public Guid? DomainId { get; set; }

    /// <summary>Canonical (trimmed, lower-case), like every address in this database.</summary>
    [Column("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>nonce(12) + tag(16) + AES-256-GCM(password). Never logged, never returned.</summary>
    [Column("cipher")]
    public byte[] Cipher { get; set; } = [];

    [Column("creation_date")]
    public DateTime CreationDate { get; set; }
}
