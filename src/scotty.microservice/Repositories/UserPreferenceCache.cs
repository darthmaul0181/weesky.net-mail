using Microsoft.Extensions.Caching.Memory;
using weesky.Scotty.Microservice.Data.Preferences;

namespace weesky.Scotty.Microservice.Repositories;

/// <summary>
/// An account's preference rows, kept a minute: every avatar on a page asks whether avatars are on.
/// Every write to those rows must call <see cref="Forget"/>, or the next reads answer the old value.
/// In memory, per instance — the trade <c>DavAuthenticationCache</c> makes.
/// </summary>
internal sealed class UserPreferenceCache(IMemoryCache cache)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    private long generation;

    /// <summary>Taken before a read: a write landing during it moves the counter, and the read is not kept.</summary>
    public long Generation => Interlocked.Read(ref generation);

    public bool TryGet(Guid userId, out IReadOnlyList<UserPreference> rows) =>
        cache.TryGetValue(Key(userId), out rows!);

    public void Store(Guid userId, IReadOnlyList<UserPreference> rows, long takenAt)
    {
        if (takenAt == Generation) cache.Set(Key(userId), rows, Lifetime);
    }

    public void Forget(Guid userId)
    {
        Interlocked.Increment(ref generation);
        cache.Remove(Key(userId));
    }

    private static (string, Guid) Key(Guid userId) => ("preferences", userId);
}
