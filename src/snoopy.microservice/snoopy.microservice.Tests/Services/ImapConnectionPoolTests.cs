using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The pool on the wire: what the server sees is the truth. Test 1 of the spec — two identities
/// never share a socket — comes first because it guards the only grave fault this work can have.
/// </summary>
public sealed class ImapConnectionPoolTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    [Fact]
    public async Task Borrow_NeverHandsAnotherCredentialsSocketOver()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;

        await using (var session = (await pool.BorrowAsync(PoolTestHost.Connection(server, "alice@weesky.be", "hunter2"), Alice, CancellationToken.None)).Value) { }
        await using (var session = (await pool.BorrowAsync(PoolTestHost.Connection(server, "bob@weesky.be", "swordfish"), Bob, CancellationToken.None)).Value) { }
        await using (var session = (await pool.BorrowAsync(PoolTestHost.Connection(server, "alice@weesky.be", "changed"), Alice, CancellationToken.None)).Value) { }

        Assert.Equal(3, server.Logins);
    }

    [Fact]
    public async Task Borrow_ReusesTheSocketReturnedByThePreviousRequest()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        for (var i = 0; i < 3; i++)
            await using (var session = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }

        Assert.Equal(1, server.Logins);
        Assert.Equal(2, pool.Snapshot().Reused);
    }

    [Fact]
    public async Task ParallelBorrows_GetDistinctSocketsAndTheOverflowIsSingleUse()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server, o => o.PoolMaxPerIdentity = 2);
        var pool = host.Pool;
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        var sessions = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            pool.BorrowAsync(alice, Alice, CancellationToken.None)));

        Assert.All(sessions, s => Assert.True(s.IsSuccess));
        Assert.Equal(3, server.Logins);
        Assert.Equal(1, pool.Snapshot().SingleUse);

        foreach (var session in sessions) await session.Value.DisposeAsync();

        Assert.Equal(2, pool.Snapshot().Idle);
        Assert.True(await server.WaitUntilAsync(() => server.Logouts == 1), "the single-use socket must LOGOUT");
    }

    [Fact]
    public async Task Borrow_WhenTheTotalCapIsReached_EvictsTheOldestIdleSocket()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server, o => o.PoolMaxTotal = 2);
        var (pool, clock) = (host.Pool, host.Clock);

        await using (var s = (await pool.BorrowAsync(PoolTestHost.Connection(server, "a@weesky.be", "a"), Alice, CancellationToken.None)).Value) { }
        clock.Now += TimeSpan.FromSeconds(1);
        await using (var s = (await pool.BorrowAsync(PoolTestHost.Connection(server, "b@weesky.be", "b"), Bob, CancellationToken.None)).Value) { }
        clock.Now += TimeSpan.FromSeconds(1);
        await using (var s = (await pool.BorrowAsync(PoolTestHost.Connection(server, "c@weesky.be", "c"), Alice, CancellationToken.None)).Value) { }

        Assert.Equal(3, server.Logins);
        Assert.Equal(1, pool.Snapshot().Evicted);
        Assert.Equal(2, pool.Snapshot().Idle);
        Assert.True(await server.WaitUntilAsync(() => server.Logouts == 1));
    }

    [Fact]
    public async Task Borrow_WithThePoolDisabled_AuthenticatesEveryTime()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server, o => o.PoolEnabled = false);
        var pool = host.Pool;
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }

        Assert.Equal(2, server.Logins);
        Assert.Equal(0, pool.Snapshot().Idle);
    }

    [Fact]
    public async Task Revoke_ClosesTheIdleSocketsAndRefusesToPoolTheOneStillOut()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        var first = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value;
        var stillOut = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value;
        await first.DisposeAsync();

        Assert.Equal(2, server.Logins);
        Assert.Equal(1, pool.Snapshot().Idle);
        Assert.Equal(1, pool.Snapshot().Borrowed);

        pool.Revoke(Alice);

        Assert.Equal(0, pool.Snapshot().Idle);
        Assert.True(await server.WaitUntilAsync(() => server.Logouts == 1), "the idle socket must LOGOUT");

        await stillOut.DisposeAsync();

        Assert.Equal(0, pool.Snapshot().Idle); // the revoked credential never gets pooled again
        Assert.True(await server.WaitUntilAsync(() => server.Logouts == 2), "the returned socket must LOGOUT");
    }

    [Fact]
    public async Task Borrow_WhenAuthenticationFails_ReturnsTheFailureAndHoldsNoPlace()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server, o => o.PoolMaxPerIdentity = 1);
        var pool = host.Pool;
        var unreachable = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2") with { ImapPort = 1 };

        var failed = await pool.BorrowAsync(unreachable, Alice, CancellationToken.None);
        // Same key, and the cap is 1: a reservation the first failure kept would send this one single-use.
        var failedAgain = await pool.BorrowAsync(unreachable, Alice, CancellationToken.None);
        var opened = await pool.BorrowAsync(PoolTestHost.Connection(server, "alice@weesky.be", "hunter2"), Alice, CancellationToken.None);

        Assert.True(failed.IsFailure);
        Assert.True(failedAgain.IsFailure);
        Assert.True(opened.IsSuccess);
        Assert.Equal(0, pool.Snapshot().SingleUse); // the failed reservation was given back
        await opened.Value.DisposeAsync();
    }

    // A cancelled open throws rather than failing: without a give-back the reserved place is held
    // until restart, and after PoolMaxPerIdentity aborts the identity is single-use for ever.
    [Fact]
    public async Task Borrow_WhenTheOpenIsCancelled_GivesTheReservedPlaceBack()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server, o => o.PoolMaxPerIdentity = 1);
        var pool = host.Pool;
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pool.BorrowAsync(alice, Alice, aborted.Token));
        var second = await pool.BorrowAsync(alice, Alice, CancellationToken.None);

        Assert.True(second.IsSuccess);
        await second.Value.DisposeAsync();
        Assert.Equal(0, pool.Snapshot().SingleUse); // the place was free: pooled, not degraded
        Assert.Equal(1, pool.Snapshot().Idle);
    }
}
