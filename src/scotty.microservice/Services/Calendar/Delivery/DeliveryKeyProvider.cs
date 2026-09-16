using weesky.Scotty.Microservice.Repositories;

namespace weesky.Scotty.Microservice.Services.Calendar.Delivery;

/// <summary>
/// A <see cref="CachedSingleton{T}"/> over a scoped store, hence the scope per load. The cache
/// lives in this process: the admin screen's invalidation reaches no other instance.
/// </summary>
internal sealed class DeliveryKeyProvider(IServiceScopeFactory scopes, ILogger<DeliveryKeyProvider> logger)
    : IDeliveryKeyProvider
{
    private readonly CachedSingleton<DeliveryKeySnapshot> cache = new(ct => LoadAsync(scopes, ct));

    public Task<DeliveryKeySnapshot?> GetAsync(CancellationToken cancellationToken) => cache.GetAsync(cancellationToken);

    public async Task InvalidateAsync(CancellationToken cancellationToken)
    {
        if (await cache.InvalidateAsync(cancellationToken) is { } ex)
            logger.LogWarning(ex, "The delivery key could not be reloaded; the next delivery call retries");
    }

    private static async Task<DeliveryKeySnapshot?> LoadAsync(IServiceScopeFactory scopes, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<IDeliveryKeyStore>().FindAsync(cancellationToken);
        return row is null ? null : new DeliveryKeySnapshot(row.KeyHash, row.Enabled);
    }
}
