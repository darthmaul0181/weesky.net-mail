namespace weesky.Scotty.Microservice.Services.Calendar;

/// <summary>
/// One value, loaded once and kept in memory until invalidated — the cache both
/// <c>ServiceAccountProvider</c> and <c>DeliveryKeyProvider</c> sit on. The cache lives in this
/// process: the admin screen's invalidation reaches no other instance.
/// </summary>
internal sealed class CachedSingleton<T>(Func<CancellationToken, Task<T?>> load)
    where T : class
{
    private sealed record Loaded(T? Value);

    private readonly SemaphoreSlim loading = new(1, 1);
    private readonly Lock swap = new();
    private Loaded? loaded;
    private long generation;

    /// <summary>Null until a load succeeds: at startup, or after a reload that failed. True/false
    /// thereafter, by whether the loaded value is null.</summary>
    public bool? IsKnown => Volatile.Read(ref loaded) is { } known ? known.Value is not null : null;

    public async Task<T?> GetAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref loaded) is { } cached) return cached.Value;

        await loading.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref loaded) is { } loadedMeanwhile) return loadedMeanwhile.Value;

            long loadedFor;
            lock (swap) loadedFor = generation;
            var value = await load(cancellationToken);
            // An invalidation that landed during the read: this result may predate the save, so it is not kept.
            lock (swap)
                if (loadedFor == generation) loaded = new Loaded(value);
            return value;
        }
        finally
        {
            loading.Release();
        }
    }

    /// <summary>Forgets the cached value and loads the stored one again. Never throws: a failed
    /// reload leaves the state unknown, and the next <see cref="GetAsync"/> retries. Returns the
    /// exception the reload failed with, so the owner can log it with its own message.</summary>
    public async Task<Exception?> InvalidateAsync(CancellationToken cancellationToken)
    {
        lock (swap)
        {
            generation++;
            loaded = null;
        }
        try
        {
            await GetAsync(cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
