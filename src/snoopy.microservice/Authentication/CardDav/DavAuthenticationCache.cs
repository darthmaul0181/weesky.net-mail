using System.Collections.Concurrent;
using weesky.Snoopy.Microservice.Authentication.Services;

namespace weesky.Snoopy.Microservice.Authentication.CardDav;

/// <summary>
/// One entry per identifier rather than per (identifier, fingerprint) pair: there is exactly one
/// secret per account, so at most one fingerprint is ever useful, and that is what makes
/// <see cref="Forget"/> a single removal — an IMemoryCache cannot enumerate its keys to find an
/// account's.
/// </summary>
internal sealed class DavAuthenticationCache(TimeProvider clock) : IDavAuthenticationCache
{
    /// <summary>Modelled on the session guard's, and for the same reason. Kept equal on purpose.</summary>
    internal static readonly TimeSpan Window = SessionGuard.CacheWindow;

    internal static readonly TimeSpan TouchInterval = TimeSpan.FromHours(1);

    private readonly record struct Entry(string Fingerprint, DavIdentity Identity, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> touched = new();

    public bool TryGet(string identifier, string fingerprint, out DavIdentity identity)
    {
        identity = default;
        if (!entries.TryGetValue(identifier, out var entry)) return false;

        if (entry.ExpiresAt <= clock.GetUtcNow())
        {
            entries.TryRemove(identifier, out _);
            return false;
        }

        if (!string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal)) return false;

        identity = entry.Identity;
        return true;
    }

    public void Store(string identifier, string fingerprint, DavIdentity identity) =>
        entries[identifier] = new Entry(fingerprint, identity, clock.GetUtcNow().Add(Window));

    public void Forget(string identifier)
    {
        entries.TryRemove(identifier, out _);
    }

    public bool ShouldTouch(Guid userId)
    {
        var now = clock.GetUtcNow();
        var previous = touched.GetOrAdd(userId, DateTimeOffset.MinValue);
        if (now - previous < TouchInterval) return false;

        // A lost race writes one extra row, which is the whole cost of not locking here.
        touched[userId] = now;
        return true;
    }
}
