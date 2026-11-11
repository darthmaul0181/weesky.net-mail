using weesky.Scotty.Microservice.Data.Preferences;

namespace weesky.Scotty.Microservice.Repositories;

internal sealed class CachedUserPreferenceStore(IUserPreferenceStore inner, UserPreferenceCache cache)
    : IUserPreferenceStore
{
    public async Task<IReadOnlyList<UserPreference>> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (cache.TryGet(userId, out var cached)) return cached;

        var takenAt = cache.Generation;
        var rows = await inner.GetAsync(userId, cancellationToken);
        cache.Store(userId, rows, takenAt);
        return rows;
    }

    public async Task SetAsync(Guid userId, string key, string value, CancellationToken cancellationToken)
    {
        try
        {
            await inner.SetAsync(userId, key, value, cancellationToken);
        }
        finally
        {
            cache.Forget(userId);
        }
    }
}
