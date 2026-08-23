using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace weesky.Snoopy.Microservice.Authentication.CardDav;

/// <summary>
/// In memory, per instance: the effective threshold multiplies by the number of instances, the
/// same trade <see cref="DavAuthenticationCache"/> makes and assumed for the same reason.
/// </summary>
internal sealed class AuthAttemptThrottle(TimeProvider clock)
{
    internal const int MaxFailures = 10;

    /// <summary>The keys are values the attacker chooses, so their number is capped.</summary>
    internal const int MaxTrackedKeys = 10_000;

    /// <summary>Batch target once eviction runs, so the O(n log n) sort amortises over many
    /// insertions instead of running on every request while the table is full.</summary>
    private const int LowWaterMark = (int)(MaxTrackedKeys * 0.9);

    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> failures = new(StringComparer.Ordinal);

    internal int TrackedKeys => failures.Count;

    internal bool IsBlocked(string identifier, string? address, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        var blocked = false;
        var now = clock.GetUtcNow();

        foreach (var key in Keys(identifier, address))
        {
            if (!failures.TryGetValue(key, out var stamps)) continue;

            DateTimeOffset oldest;
            int count;
            lock (stamps)
            {
                Prune(stamps, now);
                count = stamps.Count;
                oldest = count == 0 ? now : stamps.Peek();
            }

            if (count < MaxFailures) continue;

            blocked = true;
            // What is left of the window on the oldest failure still counted: once it falls out,
            // the key is under the threshold again.
            var left = Window - (now - oldest);
            if (left > retryAfter) retryAfter = left;
        }

        return blocked;
    }

    internal void RecordFailure(string identifier, string? address)
    {
        var now = clock.GetUtcNow();
        EvictIfFull(now);

        foreach (var key in Keys(identifier, address)) RecordFailureForKey(key, now);
    }

    /// <summary>
    /// Stops enqueuing once <see cref="MaxFailures"/> stamps survive pruning: past the threshold
    /// the key is blocked either way, and retryAfter still derives from the oldest surviving
    /// stamp, so the cap bounds one key's memory regardless of attack volume without changing any
    /// decision.
    /// </summary>
    private void RecordFailureForKey(string key, DateTimeOffset now)
    {
        while (true)
        {
            var stamps = failures.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
            lock (stamps)
            {
                // A concurrent RecordSuccess or eviction can remove this key between GetOrAdd and
                // the lock; retry against whatever is current instead of writing into an orphan.
                if (!failures.TryGetValue(key, out var current) || !ReferenceEquals(current, stamps))
                    continue;

                Prune(stamps, now);
                if (stamps.Count < MaxFailures) stamps.Enqueue(now);
                return;
            }
        }
    }

    /// <summary>
    /// Clears the identifier's count, and only it: the real phone retrying behind an attacker must
    /// get back in, while the address the attack came from is not absolved by a success elsewhere.
    /// </summary>
    internal void RecordSuccess(string identifier) => failures.TryRemove(IdentifierKey(identifier), out _);

    private static IEnumerable<string> Keys(string identifier, string? address)
    {
        yield return IdentifierKey(identifier);
        var addressKey = AddressKey(address);
        if (addressKey is not null) yield return addressKey;
    }

    private static string IdentifierKey(string identifier) => $"id:{identifier.Trim().ToLowerInvariant()}";

    /// <summary>
    /// IPv6 addresses collapse to their /64 — the routine allocation unit — so an attacker cannot
    /// spray 2^64 addresses to spend the table or dodge the counter for free. IPv4 addresses, and
    /// anything that fails to parse, keep their raw string; a household sharing a /64 shares one
    /// counter, which ten failures in fifteen minutes is not a threshold ordinary use reaches.
    /// </summary>
    private static string? AddressKey(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;

        if (IPAddress.TryParse(address, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = parsed.GetAddressBytes();
            for (var i = 8; i < bytes.Length; i++) bytes[i] = 0;
            return $"ip:{new IPAddress(bytes)}/64";
        }

        return $"ip:{address}";
    }

    private static void Prune(Queue<DateTimeOffset> stamps, DateTimeOffset now)
    {
        while (stamps.Count > 0 && now - stamps.Peek() >= Window) stamps.Dequeue();
    }

    /// <summary>
    /// Drops the keys whose newest failure is oldest. Expired keys go first — they cost nothing to
    /// lose — and if that is not enough, evicts live keys in one batch down to
    /// <see cref="LowWaterMark"/> rather than one key per call, which under-counts one attacker
    /// rather than growing without bound or re-sorting the whole table on every request.
    /// </summary>
    private void EvictIfFull(DateTimeOffset now)
    {
        if (failures.Count < MaxTrackedKeys) return;

        foreach (var (key, stamps) in failures)
        {
            bool empty;
            lock (stamps)
            {
                Prune(stamps, now);
                empty = stamps.Count == 0;
            }
            if (empty) failures.TryRemove(key, out _);
        }

        if (failures.Count <= LowWaterMark) return;

        var oldest = failures
            .Select(pair => (pair.Key, Newest: Newest(pair.Value)))
            .OrderBy(pair => pair.Newest)
            .Take(failures.Count - LowWaterMark)
            .Select(pair => pair.Key);
        foreach (var key in oldest) failures.TryRemove(key, out _);
    }

    /// <summary>The newest stamp is the queue's tail by construction — no need to search for it.</summary>
    private static DateTimeOffset Newest(Queue<DateTimeOffset> stamps)
    {
        lock (stamps)
        {
            var newest = DateTimeOffset.MinValue;
            foreach (var stamp in stamps) newest = stamp;
            return newest;
        }
    }
}
