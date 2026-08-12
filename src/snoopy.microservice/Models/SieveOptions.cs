namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// Configuration for the ManageSieve client used to manage user Sieve scripts (Pigeonhole).
/// </summary>
public sealed class SieveOptions
{
    /// <summary>
    /// Hostname of the ManageSieve server (typically the Dovecot host).
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// TCP port of the ManageSieve service. Default 4190.
    /// </summary>
    public int Port { get; set; } = 4190;

    /// <summary>
    /// Name of the Sieve script managed by this service. A single named script holds all the rules
    /// produced by the structured editor; users can keep other scripts they uploaded separately.
    /// </summary>
    public string ScriptName { get; set; } = "weesky-rules";

    /// <summary>
    /// Connect/read/write timeout in seconds for a ManageSieve session.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// When true, accept any TLS certificate (development only).
    /// </summary>
    public bool AllowInvalidCertificate { get; set; }

    /// <summary>
    /// When true, carry on over an unencrypted socket if the server does not advertise
    /// STARTTLS. Off by default, and it must stay off anywhere the link is not a loopback:
    /// the SASL PLAIN payload carries the user's own password, and base64 is not encryption. The
    /// capability banner arrives in the clear, so an attacker on the path can strip STARTTLS
    /// from it — without this gate that downgrade is silent and the password goes out plain.
    /// </summary>
    public bool AllowCleartext { get; set; }
}
