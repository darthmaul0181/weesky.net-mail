using System.Collections.Concurrent;

namespace weesky.Snoopy.Microservice.Authentication.CardDav;

/// <summary>
/// Bounds password guessing on the synchronisation edge. The random delay after a failure blurs
/// the timing oracle; it does not bound anything for someone opening a thousand connections at
/// once. This does — and by bounding the number of samples an attacker can average, it is also
/// what makes that delay sufficient.
///
/// Two key spaces, both counted: the identifier, for one account attacked from everywhere, and the
/// address, for every account attacked from one machine. The address is the one ForwardedHeaders
/// restored — never the raw header, which forges freely.
///
/// In memory, per instance: the effective threshold multiplies by the number of instances, the
/// same trade the burst cache makes and assumed for the same reason.
/// </summary>
internal sealed class AuthAttemptThrottle(TimeProvider clock)
{
    internal const int MaxFailures = 10;

    /// <summary>The keys are values the attacker chooses, so their number is capped.</summary>
    internal const int MaxTrackedKeys = 10_000;

    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> failures = new(StringComparer.Ordinal);

    internal int TrackedKeys => failures.Count;

    internal bool IsBlocked(string identifier, string? address, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
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

            // What is left of the window on the oldest failure still counted: once it falls out,
            // the key is under the threshold again.
            var left = Window - (now - oldest);
            if (left > retryAfter) retryAfter = left;
        }

        return retryAfter > TimeSpan.Zero;
    }

    internal void RecordFailure(string identifier, string? address)
    {
        var now = clock.GetUtcNow();
        EvictIfFull(now);

        foreach (var key in Keys(identifier, address))
        {
            var stamps = failures.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
            lock (stamps)
            {
                Prune(stamps, now);
                stamps.Enqueue(now);
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
        if (!string.IsNullOrWhiteSpace(address)) yield return $"ip:{address}";
    }

    private static string IdentifierKey(string identifier) => $"id:{identifier.Trim().ToLowerInvariant()}";

    private static void Prune(Queue<DateTimeOffset> stamps, DateTimeOffset now)
    {
        while (stamps.Count > 0 && now - stamps.Peek() >= Window) stamps.Dequeue();
    }

    /// <summary>
    /// Drops the keys whose newest failure is oldest. Expired keys go first — they cost nothing to
    /// lose — and only if that is not enough does a live key go, which under-counts one attacker
    /// rather than growing without bound.
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

        if (failures.Count < MaxTrackedKeys) return;

        var oldest = failures
            .Select(pair => (pair.Key, Newest: Newest(pair.Value)))
            .OrderBy(pair => pair.Newest)
            .Take(failures.Count - MaxTrackedKeys + 1)
            .Select(pair => pair.Key);
        foreach (var key in oldest) failures.TryRemove(key, out _);
    }

    private static DateTimeOffset Newest(Queue<DateTimeOffset> stamps)
    {
        lock (stamps) return stamps.Count == 0 ? DateTimeOffset.MinValue : stamps.Max();
    }
}
