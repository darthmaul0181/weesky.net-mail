using System.Diagnostics;
using CSharpFunctionalExtensions;
using MailKit.Net.Imap;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The only recovery path in the design sits before any business command: a NOOP under its own
/// short bound. A socket that fails it, or a session that ended in doubt, is dropped without a
/// LOGOUT — nothing in sync is there to say it to, and a second bound would double the wait.
/// </summary>
public sealed class ImapConnectionPoolHealthTests
{
    private static readonly Guid Alice = Guid.NewGuid();

    /// <summary>Waits out the grace a background close would need, then fails if a LOGOUT was sent.
    /// Read off the wire, not off the counters: a silenced connection answers none.</summary>
    private static async Task AssertNoLogoutAsync(PoolImapServer server) =>
        Assert.False(
            await server.WaitUntilAsync(
                () => server.Commands.Any(c => c.Contains("LOGOUT", StringComparison.OrdinalIgnoreCase)),
                TimeSpan.FromMilliseconds(500)),
            "a socket dropped in doubt must be closed without a LOGOUT");

    [Fact]
    public async Task Borrow_WithinTheTrustWindow_SkipsTheNoop()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var (pool, clock) = (host.Pool, host.Clock);
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
        clock.Now += TimeSpan.FromSeconds(1);
        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }

        Assert.Equal(0, server.NoOps);
    }

    [Fact]
    public async Task Borrow_PastTheTrustWindow_SendsOneNoop()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var (pool, clock) = (host.Pool, host.Clock);
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
        clock.Now += TimeSpan.FromSeconds(60);
        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }

        Assert.Equal(1, server.NoOps);
        Assert.Equal(1, server.Logins);
    }

    // Test 5 + 14 of the spec: the black hole. The server reads and never answers; the socket
    // stays open, so only the health bound can end the wait — and no LOGOUT may follow it.
    [Fact]
    public async Task Borrow_OnABlackHoleSocket_FailsOverWithinTheHealthBoundAndWithoutLogout()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var (pool, clock) = (host.Pool, host.Clock);
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
        clock.Now += TimeSpan.FromSeconds(60);
        server.SilenceOpenConnections();

        var stopwatch = Stopwatch.StartNew();
        var borrowed = await pool.BorrowAsync(alice, Alice, CancellationToken.None);
        stopwatch.Stop();

        Assert.True(borrowed.IsSuccess);
        Assert.Equal(2, server.Logins);
        Assert.Equal(1, pool.Snapshot().HealthFailures);
        Assert.Equal(0, server.Logouts);
        await AssertNoLogoutAsync(server);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(6),
            $"failover took {stopwatch.Elapsed.TotalSeconds:F1}s — must sit under the 2 s health bound plus a fresh open, not under the 10 s client timeout");

        var flagged = await borrowed.Value.SetFlagsAsync("INBOX", [1u], MailFlag.Flagged, true, CancellationToken.None);
        Assert.True(flagged.IsSuccess); // the business command ran once, on the fresh socket
        await borrowed.Value.DisposeAsync();
    }

    // The health-failure drop on a socket the server keeps answering: the black hole above cannot
    // pin it, since MailKit tears that client down itself and no LOGOUT is possible either way.
    [Fact]
    public async Task Borrow_OnASocketThatRefusesTheNoop_DropsItWithoutLogoutAndOpensAFreshOne()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var (pool, clock) = (host.Pool, host.Clock);
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
        clock.Now += TimeSpan.FromSeconds(60);
        server.RefuseNoop();

        var borrowed = await pool.BorrowAsync(alice, Alice, CancellationToken.None);

        Assert.True(borrowed.IsSuccess);
        Assert.Equal(1, pool.Snapshot().HealthFailures);
        Assert.Equal(2, server.Logins);
        Assert.Equal(0, server.Logouts);
        await AssertNoLogoutAsync(server);

        var flagged = await borrowed.Value.SetFlagsAsync("INBOX", [1u], MailFlag.Flagged, true, CancellationToken.None);
        Assert.True(flagged.IsSuccess);
        await borrowed.Value.DisposeAsync();
    }

    // A budget hot-reloaded to zero or below must not reach CancelAfter as it stands: the borrow
    // would throw with the entry stuck in _borrowed, and the background close never dispose.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Borrow_WithANonPositiveHealthTimeout_FailsTheCheckFastAndOpensAFreshSocket(int seconds)
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server, o => o.PoolHealthTimeoutSeconds = seconds);
        var (pool, clock) = (host.Pool, host.Clock);
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
        clock.Now += TimeSpan.FromSeconds(60);
        server.SilenceOpenConnections(); // only the bound can end the NOOP, so zero means at once

        var borrowed = await pool.BorrowAsync(alice, Alice, CancellationToken.None);

        Assert.True(borrowed.IsSuccess);
        Assert.Equal(1, pool.Snapshot().HealthFailures);
        Assert.Equal(2, server.Logins);
        await borrowed.Value.DisposeAsync();
        Assert.Equal(0, pool.Snapshot().Borrowed);
    }

    // Test 6 of the spec: a cancelled command leaves the protocol in doubt.
    [Fact]
    public async Task Return_OfATaintedSession_DropsTheSocketWithoutLogout()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        var session = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value;
        server.SilenceOpenConnections();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.SetFlagsAsync("INBOX", [1u], MailFlag.Flagged, true, cts.Token));
        await session.DisposeAsync();

        Assert.Equal(0, pool.Snapshot().Idle);
        Assert.Equal(0, server.Logouts);
        await AssertNoLogoutAsync(server);

        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
        Assert.Equal(2, server.Logins);
    }

    // The release verdict on its own: the socket stays connected, only healthy: false keeps it out.
    [Fact]
    public async Task Return_ReleasedUnhealthyOnALiveSocket_DropsItWithoutLogout()
    {
        using var server = new PoolImapServer();
        server.Start();
        var options = new MailOptions { TimeoutSeconds = 10, AllowCleartext = true, PoolHealthTimeoutSeconds = 2 };
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);
        var source = new ReleaseCapturingSource(new ImapConnectionFactory(
            monitor.Object, Mock.Of<IMailHtmlSanitizer>(), NullLogger<ImapConnectionFactory>.Instance));
        await using var pool = new ImapConnectionPool(
            source, new CredentialFingerprint(), monitor.Object, new MutableTimeProvider(), NullLogger<ImapConnectionPool>.Instance);
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        Assert.True((await pool.BorrowAsync(alice, Alice, CancellationToken.None)).IsSuccess);
        Assert.True(source.Client!.IsConnected);
        await source.Release!(source.Client, healthy: false);

        Assert.Equal(0, pool.Snapshot().Idle);
        await AssertNoLogoutAsync(server);

        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
        Assert.Equal(2, server.Logins);
    }

    // A tagged NO/BAD leaves the protocol in phase: a refusal costs no reconnection.
    [Fact]
    public async Task Return_AfterACommandRefusal_KeepsTheSocket()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        await using (var session = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value)
            Assert.True((await session.SetSubscriptionAsync("INBOX", true, CancellationToken.None)).IsFailure);

        Assert.Equal(1, pool.Snapshot().Idle);
        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
        Assert.Equal(1, server.Logins);
    }

    private sealed class ReleaseCapturingSource(IImapClientSource inner) : IImapClientSource
    {
        public ImapClient? Client { get; private set; }
        public ImapClientRelease? Release { get; private set; }

        public Task<Result<ImapClient>> OpenClientAsync(MailAccountConnection connection, CancellationToken cancellationToken) =>
            inner.OpenClientAsync(connection, cancellationToken);

        public IImapSession CreateSession(ImapClient client, ImapClientRelease release)
        {
            (Client, Release) = (client, release);
            return inner.CreateSession(client, release);
        }
    }

    // The counterpart: a clean sentinel — the server answered, the socket is fine — is reused.
    [Fact]
    public async Task Return_AfterASentinel_KeepsTheSocket()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        await using (var session = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value)
        {
            var missing = await session.SetFlagsAsync("Nope", [1u], MailFlag.Flagged, true, CancellationToken.None);
            Assert.Equal(ImapSession.FolderNotFound, missing.Error);
        }
        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }

        Assert.Equal(1, server.Logins);
    }
}
