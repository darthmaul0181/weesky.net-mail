namespace weesky.Snoopy.Microservice.Models;

public sealed class SieveRule
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When true, all conditions must match (Sieve <c>allof</c>); when false, any condition matches (<c>anyof</c>).
    /// Ignored when there is exactly one condition.
    /// </summary>
    public bool MatchAll { get; set; } = true;

    /// <summary>When true, no further rules are evaluated after this one fires (Sieve <c>stop;</c>).</summary>
    public bool StopAfter { get; set; }

    public List<SieveCondition> Conditions { get; set; } = new();

    public List<SieveAction> Actions { get; set; } = new();
}
