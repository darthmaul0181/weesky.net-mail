using CSharpFunctionalExtensions;
using MailKit.Net.Imap;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Exclusive leases over authenticated clients keyed by what authenticated them
/// (<see cref="PoolKey"/>), two clocks read only at borrow and return, and one rule above the
/// others: saturation degrades to a single-use connection. Spec: docs/superpowers/specs/2026-08-20-webmail-imap-connection-pool-design.md.
/// </summary>
internal sealed class ImapConnectionPool(
    IImapClientSource source,
    CredentialFingerprint fingerprint,
    IOptionsMonitor<MailOptions> options,
    TimeProvider clock,
    ILogger<ImapConnectionPool> logger) : IImapConnectionPool, IAsyncDisposable
{
    /// <summary>Returned this recently, a socket is trusted without a NOOP: the parallel burst.</summary>
    internal static readonly TimeSpan TrustWindow = TimeSpan.FromSeconds(5);

    // Verified on MailKit 4.17: ImapClient keeps no credential after AuthenticateAsync — the pooled
    // client is a capability, never a secret.
    private sealed class Entry(PoolKey key, ImapClient client, DateTimeOffset openedAt)
    {
        public PoolKey Key { get; } = key;
        public ImapClient Client { get; } = client;
        public DateTimeOffset OpenedAt { get; } = openedAt;
        public DateTimeOffset ReturnedAt { get; set; } = openedAt;
        public DateTimeOffset BorrowedAt { get; set; } = openedAt;
        public bool Borrowed { get; set; }
        /// <summary>Holds a place under the caps; false once the borrow horizon gave it back.</summary>
        public bool Counted { get; set; } = true;
        public Guid Borrower { get; set; }
        public long BorrowGeneration { get; set; }
        public HashSet<Guid> Users { get; } = [];
    }

    private readonly object _gate = new();
    private readonly Dictionary<PoolKey, List<Entry>> _idle = [];
    private readonly Dictionary<PoolKey, int> _counted = [];
    private readonly Dictionary<Guid, HashSet<Entry>> _byUser = [];
    private readonly Dictionary<Guid, long> _generation = [];
    private readonly HashSet<Entry> _borrowed = [];
    private int _countedTotal;
    private bool _disposed;
    private long _borrows, _reused, _opened, _singleUse, _healthFailures;
    private long _closedIdle, _closedLifetime, _closedAtReturn, _evicted;

    public async Task<Result<IImapSession>> BorrowAsync(
        MailAccountConnection connection, Guid userUid, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var settings = options.CurrentValue;
        if (!settings.PoolEnabled || _disposed) return await SingleUseAsync(connection, cancellationToken);

        Interlocked.Increment(ref _borrows);
        var key = PoolKey.From(connection, fingerprint);

        while (TakeIdle(key, userUid, settings) is { } entry)
        {
            bool healthy;
            try
            {
                healthy = await IsHealthyAsync(entry, settings, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Discard(entry);
                throw;
            }

            if (healthy)
            {
                Interlocked.Increment(ref _reused);
                return Result.Success(Lease(entry));
            }

            Interlocked.Increment(ref _healthFailures);
            Discard(entry);
        }

        if (!TryReserve(key, userUid, settings, out var generation))
        {
            Interlocked.Increment(ref _singleUse);
            return await SingleUseAsync(connection, cancellationToken);
        }

        Result<ImapClient> opened;
        try
        {
            opened = await source.OpenClientAsync(connection, cancellationToken);
        }
        catch
        {
            // A cancelled open throws: without this the reserved place is held until restart.
            Unreserve(key);
            throw;
        }

        if (opened.IsFailure)
        {
            Unreserve(key);
            return Result.Failure<IImapSession>(opened.Error);
        }

        Interlocked.Increment(ref _opened);
        var fresh = new Entry(key, opened.Value, clock.GetUtcNow());
        // The generation read before the open: a revocation landing during it must not be stamped over.
        lock (_gate) Lend(fresh, userUid, generation);
        return Result.Success(Lease(fresh));
    }

    public void Close(Guid userUid)
    {
        List<Entry> closing;
        lock (_gate) closing = DetachIdleLocked(userUid);
        if (closing.Count > 0) CloseInBackground(closing);
    }

    public void Revoke(Guid userUid)
    {
        List<Entry> closing;
        // One acquisition: a borrow slipping between the bump and the close would stamp the new
        // generation and be pooled again at return, outliving the revocation.
        lock (_gate)
        {
            _generation[userUid] = GenerationOf(userUid) + 1;
            closing = DetachIdleLocked(userUid);
        }
        if (closing.Count > 0) CloseInBackground(closing);
    }

    /// <summary>Under the lock: drops the user's idle entries and hands them back to close outside it.</summary>
    private List<Entry> DetachIdleLocked(Guid userUid)
    {
        if (!_byUser.TryGetValue(userUid, out var set)) return [];
        var closing = set.Where(e => !e.Borrowed).ToList();
        foreach (var entry in closing) RemoveLocked(entry);
        return closing;
    }

    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        var now = clock.GetUtcNow();
        var expired = new List<Entry>();
        lock (_gate)
        {
            foreach (var entry in _idle.Values.SelectMany(list => list).ToArray())
            {
                var idle = now - entry.ReturnedAt >= IdleTtl(settings);
                var old = now - entry.OpenedAt >= Lifetime(settings);
                if (!idle && !old && settings.PoolEnabled) continue;

                RemoveLocked(entry);
                expired.Add(entry);
                Interlocked.Increment(ref old ? ref _closedLifetime : ref _closedIdle);
            }

            // A lease past the horizon gives its place back; the socket stays with its request.
            foreach (var entry in _borrowed)
                if (entry.Counted && now - entry.BorrowedAt >= Lifetime(settings)) ReleasePlaceLocked(entry);
        }

        await CloseGracefullyAsync(expired.Select(e => e.Client), HealthTimeout(settings), cancellationToken);
        return expired.Count;
    }

    public PoolStatistics Snapshot()
    {
        lock (_gate)
            return new PoolStatistics(
                Idle: _idle.Values.Sum(list => list.Count), Borrowed: _borrowed.Count, Keys: _counted.Count,
                Borrows: Interlocked.Read(ref _borrows), Reused: Interlocked.Read(ref _reused),
                Opened: Interlocked.Read(ref _opened), SingleUse: Interlocked.Read(ref _singleUse),
                HealthFailures: Interlocked.Read(ref _healthFailures),
                ClosedIdle: Interlocked.Read(ref _closedIdle), ClosedLifetime: Interlocked.Read(ref _closedLifetime),
                ClosedAtReturn: Interlocked.Read(ref _closedAtReturn), Evicted: Interlocked.Read(ref _evicted));
    }

    public async ValueTask DisposeAsync()
    {
        List<Entry> all;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            all = _idle.Values.SelectMany(list => list).ToList();
            foreach (var entry in all) RemoveLocked(entry);
        }
        await CloseGracefullyAsync(all.Select(e => e.Client), HealthTimeout(options.CurrentValue));
    }

    private IImapSession Lease(Entry entry) =>
        source.CreateSession(entry.Client, (_, healthy) => ReturnAsync(entry, healthy));

    private async Task<Result<IImapSession>> SingleUseAsync(MailAccountConnection connection, CancellationToken cancellationToken)
    {
        var opened = await source.OpenClientAsync(connection, cancellationToken);
        return opened.IsFailure
            ? Result.Failure<IImapSession>(opened.Error)
            : Result.Success(source.CreateSession(opened.Value, ImapSession.CloseAsync));
    }

    /// <summary>The most recently returned idle socket of the key, lent to the user; sockets past
    /// their absolute lifetime are closed on the way. Null when none is idle.</summary>
    private Entry? TakeIdle(PoolKey key, Guid userUid, MailOptions settings)
    {
        List<Entry>? stale = null;
        Entry? taken = null;
        lock (_gate)
        {
            if (_idle.TryGetValue(key, out var list))
            {
                while (list.Count > 0)
                {
                    var candidate = list[^1];
                    list.RemoveAt(list.Count - 1);
                    if (clock.GetUtcNow() - candidate.OpenedAt >= Lifetime(settings))
                    {
                        RemoveLocked(candidate);
                        (stale ??= []).Add(candidate);
                        Interlocked.Increment(ref _closedLifetime);
                        continue;
                    }

                    Lend(candidate, userUid);
                    taken = candidate;
                    break;
                }

                if (list.Count == 0) _idle.Remove(key);
            }
        }

        if (stale is not null) CloseInBackground(stale);
        return taken;
    }

    private async Task<bool> IsHealthyAsync(Entry entry, MailOptions settings, CancellationToken cancellationToken)
    {
        var client = entry.Client;
        if (!client.IsConnected || !client.IsAuthenticated) return false;
        client.Timeout = Math.Max(0, settings.TimeoutSeconds) * 1000;
        if (clock.GetUtcNow() - entry.ReturnedAt < TrustWindow) return true;

        using var cap = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cap.CancelAfter(HealthTimeout(settings));
        try
        {
            await client.NoOpAsync(cap.Token);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false; // timed out, refused, or gone: the caller discards it
        }
    }

    private ValueTask ReturnAsync(Entry entry, bool healthy)
    {
        bool pooled;
        lock (_gate)
        {
            var settings = options.CurrentValue;
            var now = clock.GetUtcNow();
            _borrowed.Remove(entry);
            entry.Borrowed = false;
            pooled = healthy && entry.Counted && settings.PoolEnabled && !_disposed
                     && entry.Client.IsConnected
                     && entry.BorrowGeneration == GenerationOf(entry.Borrower)
                     && now - entry.OpenedAt < Lifetime(settings);
            if (pooled)
            {
                entry.ReturnedAt = now;
                if (!_idle.TryGetValue(entry.Key, out var list)) _idle[entry.Key] = list = [];
                list.Add(entry);
            }
            else RemoveLocked(entry);
        }

        if (pooled) return ValueTask.CompletedTask;
        Interlocked.Increment(ref _closedAtReturn);
        if (healthy) CloseInBackground([entry]);
        else entry.Client.Dispose(); // dead or out of sync: nothing is there to say LOGOUT to
        return ValueTask.CompletedTask;
    }

    /// <summary>Under the lock: marks the entry lent to the user and stamps their generation.</summary>
    private void Lend(Entry entry, Guid userUid) => Lend(entry, userUid, GenerationOf(userUid));

    private void Lend(Entry entry, Guid userUid, long generation)
    {
        entry.Borrowed = true;
        entry.BorrowedAt = clock.GetUtcNow();
        entry.Borrower = userUid;
        entry.BorrowGeneration = generation;
        entry.Users.Add(userUid);
        if (!_byUser.TryGetValue(userUid, out var set)) _byUser[userUid] = set = [];
        set.Add(entry);
        _borrowed.Add(entry);
    }

    private bool TryReserve(PoolKey key, Guid userUid, MailOptions settings, out long generation)
    {
        Entry? evicted = null;
        lock (_gate)
        {
            generation = GenerationOf(userUid);
            _counted.TryGetValue(key, out var perIdentity);
            if (perIdentity >= settings.PoolMaxPerIdentity) return false;
            if (_countedTotal >= settings.PoolMaxTotal && (evicted = EvictOldestIdleLocked()) is null) return false;
            // Re-read: evicting an entry of this very key gave one of its places back just above.
            _counted[key] = (_counted.TryGetValue(key, out var held) ? held : 0) + 1;
            _countedTotal++;
        }

        if (evicted is not null)
        {
            Interlocked.Increment(ref _evicted);
            CloseInBackground([evicted]);
        }
        return true;
    }

    private void Unreserve(PoolKey key)
    {
        lock (_gate) DropPlaceLocked(key);
    }

    private Entry? EvictOldestIdleLocked()
    {
        var oldest = _idle.Values.SelectMany(list => list).MinBy(e => e.ReturnedAt);
        if (oldest is null) return null;
        RemoveLocked(oldest);
        return oldest;
    }

    private void Discard(Entry entry)
    {
        lock (_gate) RemoveLocked(entry);
        entry.Client.Dispose();
    }

    private void RemoveLocked(Entry entry)
    {
        if (_idle.TryGetValue(entry.Key, out var list))
        {
            list.Remove(entry);
            if (list.Count == 0) _idle.Remove(entry.Key);
        }
        _borrowed.Remove(entry);
        foreach (var uid in entry.Users)
            if (_byUser.TryGetValue(uid, out var set) && set.Remove(entry) && set.Count == 0) _byUser.Remove(uid);
        ReleasePlaceLocked(entry);
    }

    private void ReleasePlaceLocked(Entry entry)
    {
        if (!entry.Counted) return;
        entry.Counted = false;
        DropPlaceLocked(entry.Key);
    }

    /// <summary>A key nobody counts any more leaves the dictionary: credentials rotate, and this
    /// singleton outlives weeks of them.</summary>
    private void DropPlaceLocked(PoolKey key)
    {
        if (--_counted[key] == 0) _counted.Remove(key);
        _countedTotal--;
    }

    private long GenerationOf(Guid userUid) => _generation.TryGetValue(userUid, out var generation) ? generation : 0;

    private void CloseInBackground(IEnumerable<Entry> entries) =>
        _ = CloseGracefullyAsync(entries.Select(e => e.Client).ToArray(), HealthTimeout(options.CurrentValue));

    /// <summary>Polite LOGOUTs in parallel under one budget; whatever is still pending past it is cut.</summary>
    private async Task CloseGracefullyAsync(
        IEnumerable<ImapClient> clients, TimeSpan budget, CancellationToken cancellationToken = default)
    {
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cap.CancelAfter(budget);
        await Task.WhenAll(clients.Select(async client =>
        {
            try
            {
                if (client.IsConnected) await client.DisconnectAsync(quit: true, cap.Token);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "A pooled IMAP connection did not close politely");
            }
            finally
            {
                client.Dispose();
            }
        }));
    }

    // Clamped: startup validation refuses a negative, a hot reload does not, and CancelAfter throws on one.
    private static TimeSpan IdleTtl(MailOptions settings) => TimeSpan.FromSeconds(Math.Max(0, settings.PoolIdleSeconds));
    private static TimeSpan Lifetime(MailOptions settings) => TimeSpan.FromMinutes(Math.Max(0, settings.PoolMaxLifetimeMinutes));
    private static TimeSpan HealthTimeout(MailOptions settings) => TimeSpan.FromSeconds(Math.Max(0, settings.PoolHealthTimeoutSeconds));
}
