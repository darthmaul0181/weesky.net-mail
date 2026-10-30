using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Microservice.Services;

/// <summary>Looks up the public picture behind an email address.</summary>
public interface IAvatarService
{
    /// <summary>The address's picture, or null when it has none or the lookup failed.</summary>
    Task<AvatarImage?> GetAsync(string address, CancellationToken cancellationToken);
}
