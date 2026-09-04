using MailKit.Security;

namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// Connection settings for the mail server. Only connection parameters live here:
/// everything server-specific (hierarchy separator, namespaces, special-use folders,
/// capabilities) is discovered at runtime from the IMAP session, so that additional
/// domains can point at arbitrary external servers we will never hold configuration for.
/// </summary>
public sealed class MailOptions
{
    /// <summary>IMAP host name.</summary>
    public string ImapHost { get; set; } = string.Empty;

    /// <summary>IMAP port. 143 for STARTTLS, 993 for implicit TLS.</summary>
    public int ImapPort { get; set; } = 143;

    /// <summary>
    /// IMAP transport security. StartTls fails when the server does not advertise
    /// STARTTLS; StartTlsWhenAvailable silently falls back to cleartext, which on port
    /// 143 would put credentials on the wire. Prefer StartTls.
    /// </summary>
    public SecureSocketOptions ImapSecurity { get; set; } = SecureSocketOptions.StartTls;

    /// <summary>Submission host name.</summary>
    public string SmtpHost { get; set; } = string.Empty;

    /// <summary>Submission port.</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>Submission transport security.</summary>
    public SecureSocketOptions SmtpSecurity { get; set; } = SecureSocketOptions.StartTls;

    /// <summary>Maximum outgoing message size — sum of raw attachment bytes, in megabytes.
    /// Base64 adds ~35%: keep this below Postfix's message_size_limit accordingly.</summary>
    public int MaxMessageSizeMb { get; set; } = 25;

    /// <summary>How long a staged attachment survives without being sent, in hours.</summary>
    public int StagedAttachmentTtlHours { get; set; } = 12;

    /// <summary>Connect and command timeout, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Accept invalid server certificates. Development only — logged as a warning on
    /// every connection when enabled.
    /// </summary>
    public bool AllowInvalidCertificate { get; set; }

    /// <summary>
    /// When true, accept an external domain whose stored transport security is
    /// <see cref="SecureSocketOptions.None"/>, and authenticate over a socket that turned out not
    /// to be encrypted. Off by default, and it must stay off anywhere the link is not a loopback:
    /// the account's own mail password crosses that socket in the clear.
    ///
    /// The refusal is decided from the connected client, not from the configured value, so
    /// <c>Auto</c> and <c>StartTlsWhenAvailable</c> falling back to cleartext — or an attacker
    /// stripping STARTTLS from the banner — are caught too, and every cleartext connection that
    /// this flag permits is logged as a warning naming the host.
    ///
    /// This governs IMAP and SMTP only. ManageSieve has its own <c>Sieve:AllowCleartext</c>.
    /// </summary>
    public bool AllowCleartext { get; set; }

    /// <summary>Whether authenticated IMAP connections are kept between requests. Read on every
    /// borrow and every sweep, so switching it off takes effect without a restart.</summary>
    public bool PoolEnabled { get; set; } = true;

    /// <summary>Idle time before a pooled connection is closed. Above the frontend's 60 s poll on purpose.</summary>
    public int PoolIdleSeconds { get; set; } = 70;

    /// <summary>Absolute lifetime of a pooled connection: the bound on how long a revoked credential keeps working.</summary>
    public int PoolMaxLifetimeMinutes { get; set; } = 15;

    /// <summary>Connections per (host, port, security, user, credential). Keep well under Dovecot's
    /// mail_max_userip_connections (10): this service is one IP.</summary>
    public int PoolMaxPerIdentity { get; set; } = 4;

    /// <summary>Pooled connections in this process, all identities together.</summary>
    public int PoolMaxTotal { get; set; } = 200;

    /// <summary>Bound on the NOOP that checks a pooled connection before reuse, and on a polite LOGOUT.</summary>
    public int PoolHealthTimeoutSeconds { get; set; } = 3;

    /// <summary>Where the callback sends the browser back to, e.g. https://account.mail.weesky.net.
    /// The settings page's path is appended by the controller.</summary>
    public string WebmailBaseUrl { get; set; } = string.Empty;

    /// <summary>The redirect URI registered with every provider. Must match byte for byte, which
    /// is why it is configured rather than rebuilt from the incoming request.</summary>
    public string OAuthRedirectUri { get; set; } = string.Empty;

    /// <summary>True when enough is configured to attempt an IMAP connection.</summary>
    public bool IsImapConfigured => !string.IsNullOrWhiteSpace(ImapHost);

    /// <summary>True when enough is configured to attempt an SMTP connection.</summary>
    public bool IsSmtpConfigured => !string.IsNullOrWhiteSpace(SmtpHost);
}
