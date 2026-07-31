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

    /// <summary>True when enough is configured to attempt an IMAP connection.</summary>
    public bool IsImapConfigured => !string.IsNullOrWhiteSpace(ImapHost);

    /// <summary>True when enough is configured to attempt an SMTP connection.</summary>
    public bool IsSmtpConfigured => !string.IsNullOrWhiteSpace(SmtpHost);
}
