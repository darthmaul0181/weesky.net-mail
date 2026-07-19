namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// Wrapper used by the raw Sieve script endpoints (<c>GET/PUT /api/Rules/raw</c>) so the
/// payload remains a JSON object rather than a bare string.
/// </summary>
public sealed class SieveRawScript
{
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Optional ManageSieve script name to write to. When null, the server's default
    /// (currently <c>weesky-rules</c>) is used. Echo back the value returned by GET
    /// to keep editing the same script.
    /// </summary>
    public string? ScriptName { get; set; }
}
