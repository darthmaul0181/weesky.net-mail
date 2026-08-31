using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
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

    // Keyed by the resolved user id, never a caller-supplied string, so its size is bounded by
    // the account count — not swept, because nothing here lets a caller grow it unboundedly.
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> touched = new();

    // Outside the entries on purpose: an expiring entry would take its generation with it, and the
    // next Store would then be accepted under a stale one. Only Forget adds a key, and Forget is
    // reachable only for an account that exists, so this is bounded by the account count too.
    private readonly ConcurrentDictionary<string, long> generations = new(StringComparer.Ordinal);

    internal int TrackedGenerations => generations.Count;

    public bool TryGet(string identifier, string fingerprint, out DavIdentity identity)
    {
        identity = default;
        if (!entries.TryGetValue(identifier, out var entry)) return false;

        if (entry.ExpiresAt <= clock.GetUtcNow())
        {
            // Compare-and-remove: a concurrent Store publishing a fresh entry between the read
            // above and here must not be the one this call deletes.
            entries.TryRemove(new KeyValuePair<string, Entry>(identifier, entry));
            return false;
        }

        if (!FingerprintsMatch(entry.Fingerprint, fingerprint)) return false;

        identity = entry.Identity;
        return true;
    }

    public long Generation(string identifier) => generations.TryGetValue(identifier, out var value) ? value : 0;

    public void Store(string identifier, string fingerprint, DavIdentity identity, long generation)
    {
        if (Generation(identifier) != generation) return;

        var entry = new Entry(fingerprint, identity, clock.GetUtcNow().Add(Window));
        entries[identifier] = entry;

        // A Forget landing between the check and the write above would otherwise leave standing
        // the very entry it came to remove. Forget moves the generation first and removes second,
        // so either this second read sees the move, or that removal follows this write: whichever
        // order the two interleave in, the revoked entry is gone.
        if (Generation(identifier) != generation)
            entries.TryRemove(new KeyValuePair<string, Entry>(identifier, entry));
    }

    public void Forget(string identifier)
    {
        // These two lines are ordered, and no test can catch a swap: the generation moves FIRST so
        // that a Store racing this Forget either reads the new generation and withdraws its entry,
        // or writes after the removal below and is withdrawn by its own second check. Removing
        // first would leave a window where Store publishes a revoked secret for the cache's minute.
        generations.AddOrUpdate(identifier, 1, (_, previous) => previous + 1);
        entries.TryRemove(identifier, out _);
    }

    public bool ShouldTouch(Guid userId)
    {
        var now = clock.GetUtcNow();
        if (touched.TryGetValue(userId, out var previous) && now - previous < TouchInterval) return false;

        // A lost race writes one extra row, which is the whole cost of not locking here.
        touched[userId] = now;
        return true;
    }

    /// <summary>Same discipline as <see cref="weesky.Snoopy.Microservice.Services.DavSecret.Matches"/>, on the same kind of digest.</summary>
    private static bool FingerprintsMatch(string stored, string presented)
    {
        if (stored.Length != presented.Length) return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(stored), Encoding.ASCII.GetBytes(presented));
    }
}
