using System.ComponentModel.DataAnnotations.Schema;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// An external mail provider an admin allows users to connect a mailbox from. Holds the
/// connection parameters we will never read from a live session for a foreign server, the way
/// the home server's are discovered — a user supplies only credentials, never a host.
/// </summary>
[Table("external_domains")]
public sealed class ExternalDomain
{
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>Display name ("Gmail"); unique, and the table collates in binary.</summary>
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("imap_host")]
    public string ImapHost { get; set; } = string.Empty;

    [Column("imap_port")]
    public int ImapPort { get; set; }

    /// <summary>None | StartTls | SslOnConnect.</summary>
    [Column("imap_security")]
    public string ImapSecurity { get; set; } = "StartTls";

    [Column("smtp_host")]
    public string SmtpHost { get; set; } = string.Empty;

    [Column("smtp_port")]
    public int SmtpPort { get; set; }

    [Column("smtp_security")]
    public string SmtpSecurity { get; set; } = "StartTls";

    /// <summary>Null means the provider offers no Sieve: the rules editor stays hidden.</summary>
    [Column("sieve_host")]
    public string? SieveHost { get; set; }

    [Column("sieve_port")]
    public int? SievePort { get; set; }

    /// <summary>Password unless an admin declared this provider an OAuth one.</summary>
    [Column("auth_mode")]
    public MailAuthMode AuthMode { get; set; }

    [Column("oauth_authorization_url")]
    public string? OAuthAuthorizationUrl { get; set; }

    [Column("oauth_token_url")]
    public string? OAuthTokenUrl { get; set; }

    /// <summary>Space-separated, sent to the provider verbatim.</summary>
    [Column("oauth_scopes")]
    public string? OAuthScopes { get; set; }

    [Column("oauth_client_id")]
    public string? OAuthClientId { get; set; }

    /// <summary>Data-Protection-protected. Never logged, never returned by any endpoint.</summary>
    [Column("oauth_client_secret")]
    public byte[]? OAuthClientSecret { get; set; }

    [Column("creation_date")]
    public DateTime CreationDate { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
