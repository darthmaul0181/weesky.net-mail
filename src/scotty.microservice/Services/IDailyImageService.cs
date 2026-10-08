using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Microservice.Services;

/// <summary>Bing's image of the day, fetched once per change and held in memory per market.</summary>
public interface IDailyImageService
{
    /// <summary>Today's image, an earlier one while Bing fails, or null when none was ever fetched.</summary>
    Task<DailyImage?> GetAsync(string market, CancellationToken cancellationToken);
}
