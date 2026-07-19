namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// Public description of a registered rule provider, returned by
/// <c>GET /api/Rules/providers</c>.
/// </summary>
public sealed class RuleProviderInfo
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string DefaultScriptName { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
}
