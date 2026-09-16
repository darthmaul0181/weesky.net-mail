using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Microservice.Platform;

/// <summary>The profile the platform holds for an account, beyond its address.</summary>
public interface IProfileReader
{
    /// <summary>The account's display name, null when the platform holds none.</summary>
    Task<string?> GetDisplayNameAsync(User user, CancellationToken cancellationToken);
}
