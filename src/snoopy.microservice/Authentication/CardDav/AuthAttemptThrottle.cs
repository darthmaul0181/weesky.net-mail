using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace weesky.Snoopy.Microservice.Authentication.CardDav;

/// <summary>
/// In memory, per instance: the effective threshold multiplies by the number of instances, the
/// same trade <see cref="DavAuthenticationCache"/> makes and assumed for the same reason.
/// </summary>
internal sealed class AuthAttemptThrottle(TimeProvider clock) : IAuthAttemptThrottle
{
    internal const int MaxFailures = 10;

    /// <summary>
    /// The keys are values the attacker chooses, so their number is capped. A soft ceiling: the
    /// eviction runs at the start of <see cref="RecordFailure"/> and the call's two keys are added
    /// after it, so the table can settle at <c>MaxTrackedKeys + 1</c> — never assert equality.
    /// </summary>
    internal const int MaxTrackedKeys = 10_000;

    /// <summary>Batch target once eviction runs, so the sort amortises over many insertions.</summary>
    private const int LowWaterMark = (int)(MaxTrackedKeys * 0.9);

    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> failures = new(StringComparer.Ordinal);

    // One increment per successful add, one decrement per successful removal: exactly what
    // failures.Count answers, without the whole-table lock Count takes on every RecordFailure.
    private int keyCount;

    internal int TrackedKeys => failures.Count;

    internal int CountedKeys => Volatile.Read(ref keyCount);

    public bool IsBlocked(string identifier, string? address, out TimeSpan retryAfter)
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

    public void RecordFailure(string identifier, string? address)
    {
        var now = clock.GetUtcNow();
        EvictIfFull(now);

        foreach (var key in Keys(identifier, address)) RecordFailureForKey(key, now);
    }

    /// <summary>
    /// Stops enqueuing past <see cref="MaxFailures"/> surviving stamps: the key is blocked either
    /// way, so this bounds one key's memory without changing that decision.
    /// </summary>
    private void RecordFailureForKey(string key, DateTimeOffset now)
    {
        while (true)
        {
            if (!failures.TryGetValue(key, out var stamps))
            {
                stamps = new Queue<DateTimeOffset>();
                if (!failures.TryAdd(key, stamps)) continue;
                Interlocked.Increment(ref keyCount);
            }

            lock (stamps)
            {
                // A concurrent RecordSuccess or eviction can remove this key between the read and
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
    public void RecordSuccess(string identifier) => Remove(IdentifierKey(identifier));

    /// <summary>
    /// The regeneration seam. Same effect as <see cref="RecordSuccess"/> and a different reason:
    /// no secret was compared, a JWT was — see <see cref="IAuthAttemptThrottle.ForgetIdentifier"/>
    /// for why the address key is left standing.
    /// </summary>
    public void ForgetIdentifier(string identifier) => Remove(IdentifierKey(identifier));

    private static IEnumerable<string> Keys(string identifier, string? address)
    {
        yield return IdentifierKey(identifier);
        var addressKey = AddressKey(address);
        if (addressKey is not null) yield return addressKey;
    }

    private static string IdentifierKey(string identifier) => $"id:{identifier.Trim().ToLowerInvariant()}";

    /// <summary>
    /// IPv6 addresses collapse to their /64. An IPv4-mapped IPv6 address (Kestrel's shape for an
    /// IPv4 peer on a dual-stack socket) unmaps first, so it keys with its plain IPv4 form instead
    /// of masking away the address it carries. Anything unparsable keeps its raw string.
    /// </summary>
    private static string? AddressKey(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        if (!IPAddress.TryParse(address, out var parsed)) return $"ip:{address}";

        if (parsed.IsIPv4MappedToIPv6) parsed = parsed.MapToIPv4();
        if (parsed.AddressFamily != AddressFamily.InterNetworkV6) return $"ip:{parsed}";

        var bytes = parsed.GetAddressBytes();
        for (var i = 8; i < bytes.Length; i++) bytes[i] = 0;
        return $"ip6:{new IPAddress(bytes)}";
    }

    private static void Prune(Queue<DateTimeOffset> stamps, DateTimeOffset now)
    {
        while (stamps.Count > 0 && now - stamps.Peek() >= Window) stamps.Dequeue();
    }

    /// <summary>
    /// Drops the keys whose newest failure is oldest. Expired keys go first — they cost nothing to
    /// lose — and if that is not enough, evicts live keys in one batch down to
    /// <see cref="LowWaterMark"/> rather than one key per call, which under-counts one attacker
    /// rather than growing without bound or re-sorting the whole table on every request. The
    /// common case is the first line and nothing else: <c>ConcurrentDictionary.Count</c> locks
    /// every bucket, and this runs on the hot path of the very traffic the throttle is for.
    /// </summary>
    private void EvictIfFull(DateTimeOffset now)
    {
        if (Volatile.Read(ref keyCount) < MaxTrackedKeys) return;

        foreach (var (key, stamps) in failures)
        {
            bool empty;
            lock (stamps)
            {
                Prune(stamps, now);
                empty = stamps.Count == 0;
            }
            if (empty) Remove(key);
        }

        var live = failures.Count;
        if (live <= LowWaterMark) return;

        var oldest = failures
            .Select(pair => (pair.Key, Newest: Newest(pair.Value)))
            .OrderBy(pair => pair.Newest)
            .Take(live - LowWaterMark)
            .Select(pair => pair.Key);
        foreach (var key in oldest) Remove(key);
    }

    private void Remove(string key)
    {
        if (failures.TryRemove(key, out _)) Interlocked.Decrement(ref keyCount);
    }

    /// <summary>Walks to the queue's tail — the newest stamp by construction — instead of computing a max.</summary>
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
