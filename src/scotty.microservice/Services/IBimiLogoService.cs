using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Microservice.Services;

/// <summary>Looks up the brand logo a domain publishes through BIMI.</summary>
public interface IBimiLogoService
{
    /// <summary>The domain's logo as a PNG, or null when it has none, an unsafe one, or the lookup failed.</summary>
    Task<AvatarImage?> GetAsync(string domain, string selector, CancellationToken cancellationToken);
}
