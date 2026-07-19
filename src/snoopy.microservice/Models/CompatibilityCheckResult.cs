namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// Result of a compatibility check: whether every rule is representable by the target
/// provider, plus the per-rule reasons for any that are not.
/// </summary>
public sealed class CompatibilityCheckResult
{
    /// <summary>True when no rule is incompatible (the switch is lossless).</summary>
    public bool Compatible { get; set; }

    public List<IncompatibleRule> Incompatible { get; set; } = new();
}

/// <summary>A single rule the target provider cannot represent, with a human-readable reason.</summary>
public sealed class IncompatibleRule
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
