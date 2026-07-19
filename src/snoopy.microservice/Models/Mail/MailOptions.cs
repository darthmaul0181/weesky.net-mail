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

    /// <summary>Submission host name. Consumed when composing lands.</summary>
    public string SmtpHost { get; set; } = string.Empty;

    /// <summary>Submission port. Consumed when composing lands.</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>Submission transport security. Consumed when composing lands.</summary>
    public SecureSocketOptions SmtpSecurity { get; set; } = SecureSocketOptions.StartTls;

    /// <summary>Connect and command timeout, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Accept invalid server certificates. Development only — logged as a warning on
    /// every connection when enabled.
    /// </summary>
    public bool AllowInvalidCertificate { get; set; }

    /// <summary>True when enough is configured to attempt an IMAP connection.</summary>
    public bool IsImapConfigured => !string.IsNullOrWhiteSpace(ImapHost);
}
