namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// Body of <c>POST /api/Rules/CompatibilityCheck</c>: asks whether the supplied rules can
/// be represented by the target provider's format. Used before switching providers
/// (e.g. turning off "Extended rules") to preview which rules would be lost.
/// </summary>
public sealed class CompatibilityCheckRequest
{
    /// <summary>Target provider to test against. When null, the server's default provider is used.</summary>
    public string? ProviderId { get; set; }

    public List<SieveRule> Rules { get; set; } = new();
}
