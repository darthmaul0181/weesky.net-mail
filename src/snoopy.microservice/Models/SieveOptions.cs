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
    /// Master username configured in Dovecot (see <c>auth_master_users</c>).
    /// Used as the SASL PLAIN authentication identity while impersonating the target user.
    /// </summary>
    public string MasterUser { get; set; } = string.Empty;

    /// <summary>
    /// Master password matching <see cref="MasterUser"/>.
    /// </summary>
    public string MasterPassword { get; set; } = string.Empty;

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
}
