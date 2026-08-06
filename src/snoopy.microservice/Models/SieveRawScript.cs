using System.ComponentModel.DataAnnotations;

namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// Wrapper used by the raw Sieve script endpoints (<c>GET/PUT /api/Rules/raw</c>) so the
/// payload remains a JSON object rather than a bare string.
/// </summary>
public sealed class SieveRawScript
{
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Optional ManageSieve script name to write to. When null or empty, the server's default
    /// (currently <c>weesky-rules</c>) is used. Echo back the value returned by GET to keep
    /// editing the same script. A control character would split the line-oriented ManageSieve
    /// command this name is written into.
    /// </summary>
    [StringLength(512)]
    [RegularExpression(@"\A[^\x00-\x1F\x7F-\x9F]*\z",
        ErrorMessage = "Script name must not contain control characters")]
    public string? ScriptName { get; set; }
}