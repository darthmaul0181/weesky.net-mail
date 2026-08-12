using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Platform;

/// <summary>What the platform knows of an account's addresses beyond the primary one.</summary>
public interface IAliasDirectory
{
    /// <summary>False when the platform cannot verify ownership: free identities.</summary>
    bool EnforcesOwnership { get; }

    /// <summary>The account's live aliases (empty when <see cref="EnforcesOwnership"/> is false).</summary>
    Task<IReadOnlyList<string>> GetAddressesAsync(User user, CancellationToken cancellationToken);
}
