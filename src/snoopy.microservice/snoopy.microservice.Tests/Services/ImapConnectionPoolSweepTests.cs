using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// Two clocks, both read only at borrow and return — never mid-request — and a sweeper that
/// closes what nobody will borrow again. Every LOGOUT here is a polite one: these sockets are
/// healthy, the server deserves to hear it.
/// </summary>
public sealed class ImapConnectionPoolSweepTests
{
    private static readonly Guid Alice = Guid.NewGuid();

    // Test 7: past the idle TTL, the sweep closes it and the server sees a LOGOUT unprompted.
    [Fact]
    public async Task Sweep_ClosesASocketIdlePastTheTtl()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var (pool, clock) = (host.Pool, host.Clock);
        await using (var s = (await pool.BorrowAsync(PoolTestHost.Connection(server, "alice@weesky.be", "hunter2"), Alice, CancellationToken.None)).Value) { }

        clock.Now += TimeSpan.FromSeconds(69);
        Assert.Equal(0, await pool.SweepAsync(CancellationToken.None));
        clock.Now += TimeSpan.FromSeconds(2);
        Assert.Equal(1, await pool.SweepAsync(CancellationToken.None));

        Assert.Equal(1, server.Logouts);
        Assert.Equal(1, pool.Snapshot().ClosedIdle);
        Assert.Equal(0, pool.Snapshot().Idle);
    }

    // Test 2, second half: past the absolute lifetime, the next borrow re-authenticates.
    [Fact]
    public async Task Borrow_PastTheAbsoluteLifetime_ReplacesTheSocket()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var (pool, clock) = (host.Pool, host.Clock);
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        for (var minute = 0; minute < 14; minute++)
        {
            await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
            clock.Now += TimeSpan.FromMinutes(1);
        }
        Assert.Equal(1, server.Logins);

        clock.Now += TimeSpan.FromMinutes(2);
        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }

        Assert.Equal(2, server.Logins);
        Assert.Equal(1, pool.Snapshot().ClosedLifetime);
        Assert.True(await server.WaitUntilAsync(() => server.Logouts == 1));
    }

    // Test 8: the lifetime is never enforced on a socket a request is holding.
    [Fact]
    public async Task Sweep_LeavesABorrowedSocketAloneEvenPastItsLifetime()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var (pool, clock) = (host.Pool, host.Clock);
        var session = (await pool.BorrowAsync(PoolTestHost.Connection(server, "alice@weesky.be", "hunter2"), Alice, CancellationToken.None)).Value;

        clock.Now += TimeSpan.FromMinutes(16);
        Assert.Equal(0, await pool.SweepAsync(CancellationToken.None));
        var flagged = await session.SetFlagsAsync("INBOX", [1u], MailFlag.Flagged, true, CancellationToken.None);
        Assert.True(flagged.IsSuccess);

        await session.DisposeAsync();
        Assert.Equal(0, pool.Snapshot().Idle); // refused at return, closed politely
        Assert.True(await server.WaitUntilAsync(() => server.Logouts == 1));
    }

    // The other half of test 8: with no sweep in between, the return itself must refuse the socket.
    [Fact]
    public async Task Return_PastTheAbsoluteLifetime_DoesNotPoolTheSocket()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var (pool, clock) = (host.Pool, host.Clock);
        var session = (await pool.BorrowAsync(PoolTestHost.Connection(server, "alice@weesky.be", "hunter2"), Alice, CancellationToken.None)).Value;

        clock.Now += TimeSpan.FromMinutes(16);
        await session.DisposeAsync();

        Assert.Equal(0, pool.Snapshot().Idle);
        Assert.True(await server.WaitUntilAsync(() => server.Logouts == 1));
    }

    // The borrow horizon: a lease that never came back stops holding its place under the cap.
    [Fact]
    public async Task Sweep_PastTheHorizon_GivesALostLeasesPlaceBack()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server, o => o.PoolMaxPerIdentity = 1);
        var (pool, clock) = (host.Pool, host.Clock);
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");
        var lost = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value;

        clock.Now += TimeSpan.FromMinutes(16);
        await pool.SweepAsync(CancellationToken.None);
        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }

        Assert.Equal(0, pool.Snapshot().SingleUse); // the place was free again: pooled, not single-use
        Assert.Equal(1, pool.Snapshot().Idle);
        await lost.DisposeAsync();
        Assert.Equal(1, pool.Snapshot().Idle); // the late return did not re-enter
    }

    [Fact]
    public async Task Sweep_WithThePoolSwitchedOff_ClosesEverythingIdle()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var (pool, options) = (host.Pool, host.Options);
        await using (var s = (await pool.BorrowAsync(PoolTestHost.Connection(server, "alice@weesky.be", "hunter2"), Alice, CancellationToken.None)).Value) { }

        options.PoolEnabled = false;

        Assert.Equal(1, await pool.SweepAsync(CancellationToken.None));
        Assert.Equal(1, server.Logouts);
    }

    // A rotated credential is a new key: the dictionaries must shed the old ones, or a singleton
    // living for weeks walks a table of dead identities under its lock on every borrow.
    [Fact]
    public async Task Sweep_PastTheIdleTtl_ShedsTheKeysOfTheSocketsItClosed()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var (pool, clock) = (host.Pool, host.Clock);

        foreach (var password in new[] { "a", "b", "c" })
            await using (var s = (await pool.BorrowAsync(
                PoolTestHost.Connection(server, "alice@weesky.be", password), Alice, CancellationToken.None)).Value) { }

        Assert.Equal(3, pool.Snapshot().Keys);

        clock.Now += TimeSpan.FromSeconds(71);
        Assert.Equal(3, await pool.SweepAsync(CancellationToken.None));

        Assert.Equal(0, pool.Snapshot().Keys);
        Assert.Equal(0, pool.Snapshot().Idle);
    }

    [Fact]
    public async Task DisposeAsync_LogsOutEveryIdleSocket()
    {
        using var server = new PoolImapServer();
        server.Start();
        var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        await using (var s = (await pool.BorrowAsync(PoolTestHost.Connection(server, "a@weesky.be", "a"), Alice, CancellationToken.None)).Value) { }
        await using (var s = (await pool.BorrowAsync(PoolTestHost.Connection(server, "b@weesky.be", "b"), Alice, CancellationToken.None)).Value) { }

        await pool.DisposeAsync();

        Assert.Equal(2, server.Logouts);
        Assert.True(await server.WaitUntilAsync(() => server.Open == 0));
    }
}
