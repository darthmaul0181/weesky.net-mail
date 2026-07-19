namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// The user-facing view of their Sieve configuration: both the decoded structured rules
/// (when present) and the underlying raw script. Returned by the repository so the
/// frontend can switch between structured and advanced editing without a second fetch.
/// </summary>
public sealed class SieveRuleSet
{
    public SieveScriptKind Kind { get; init; }

    public IReadOnlyList<SieveRule> Rules { get; init; } = Array.Empty<SieveRule>();

    /// <summary>
    /// The exact text returned by the ManageSieve server, or an empty string if no
    /// script has been uploaded yet.
    /// </summary>
    public string RawScript { get; init; } = string.Empty;

    /// <summary>
    /// Identifier of the rule provider that decoded the script
    /// (<c>"weesky"</c>, <c>"rainloop"</c>, ...). Null when the script could not be
    /// recognised and the user must edit the raw Sieve.
    /// </summary>
    public string? ProviderId { get; init; }

    /// <summary>
    /// Name of the ManageSieve script the content was loaded from (so subsequent
    /// saves write back to the same script). Null when no script exists yet.
    /// </summary>
    public string? ScriptName { get; init; }
}
