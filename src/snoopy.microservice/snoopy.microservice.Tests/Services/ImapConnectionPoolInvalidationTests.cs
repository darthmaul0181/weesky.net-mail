using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// Logout closes what is idle; logout-everywhere also refuses what is out. A shared mailbox is one
/// entry for everybody who opens it with the same secret, and the generation is stamped at borrow —
/// by the borrower — so one user's purge cannot be dodged through a socket another user opened.
/// </summary>
public sealed class ImapConnectionPoolInvalidationTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    // Test 12: DELETE /Login — idle sockets closed, a borrowed one untouched and still poolable.
    [Fact]
    public async Task Close_ClosesTheUsersIdleSocketsAndLeavesTheBorrowedOneAlone()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        await using (var s = (await pool.BorrowAsync(PoolTestHost.Connection(server, "alice@weesky.be", "hunter2"), Alice, CancellationToken.None)).Value) { }
        var held = (await pool.BorrowAsync(PoolTestHost.Shared(server, "acc", "shared@weesky.be", "pw"), Alice, CancellationToken.None)).Value;

        pool.Close(Alice);

        Assert.True(await server.WaitUntilAsync(() => server.Logouts == 1));
        await held.DisposeAsync();
        Assert.Equal(1, pool.Snapshot().Idle); // no generation turned: the lease came back to the pool
    }

    // Test 10: DELETE /Login/All — the in-flight lease closes on return instead of re-entering.
    [Fact]
    public async Task Revoke_RefusesTheLeaseThatWasOutDuringThePurge()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");
        var held = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value;
        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { } // a second socket, back idle
        Assert.Equal(2, server.Logins);

        pool.Revoke(Alice);
        Assert.True(await server.WaitUntilAsync(() => server.Logouts == 1)); // the idle one
        await held.DisposeAsync();

        Assert.Equal(0, pool.Snapshot().Idle);
        Assert.True(await server.WaitUntilAsync(() => server.Logouts == 2));
        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
        Assert.Equal(3, server.Logins);
    }

    // Test 11: a shared entry, opened by Bob, borrowed by Alice, purged by Alice while she holds it.
    [Fact]
    public async Task Revoke_OnASharedEntry_BindsToTheBorrowerNotTheOpener()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        var shared = PoolTestHost.Shared(server, "acc", "shared@weesky.be", "pw");
        await using (var s = (await pool.BorrowAsync(shared, Bob, CancellationToken.None)).Value) { }
        var aliceHolds = (await pool.BorrowAsync(shared, Alice, CancellationToken.None)).Value;
        Assert.Equal(1, server.Logins); // one entry for both

        pool.Revoke(Alice);
        await aliceHolds.DisposeAsync();

        Assert.Equal(0, pool.Snapshot().Idle);
        await using (var s = (await pool.BorrowAsync(shared, Bob, CancellationToken.None)).Value) { }
        Assert.Equal(2, server.Logins);
    }

    [Fact]
    public async Task Revoke_ByOneUser_DoesNotRefuseTheOtherUsersLeaseOnTheSharedEntry()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        var shared = PoolTestHost.Shared(server, "acc", "shared@weesky.be", "pw");
        await using (var s = (await pool.BorrowAsync(shared, Alice, CancellationToken.None)).Value) { }
        var bobHolds = (await pool.BorrowAsync(shared, Bob, CancellationToken.None)).Value;
        Assert.Equal(1, server.Logins); // Alice's revocation must reach the very entry Bob holds

        pool.Revoke(Alice);
        await bobHolds.DisposeAsync();

        Assert.Equal(1, pool.Snapshot().Idle);
    }

    // Test 3: nothing on the return path closes a folder — CLOSE would expunge \Deleted mail.
    [Fact]
    public async Task Return_NeverClosesTheSelectedFolder()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        for (var i = 0; i < 2; i++)
            await using (var session = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value)
                Assert.True((await session.SetFlagsAsync("INBOX", [1u], MailFlag.Flagged, true, CancellationToken.None)).IsSuccess);

        Assert.Equal(1, server.Logins);
        Assert.Equal(0, server.Closes);
        Assert.Equal(0, server.Expunges);
        Assert.Contains(server.Commands, c => c.Contains(" SELECT ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(server.Commands, c => c.Contains(" CLOSE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Close_ForAnUnknownUser_DoesNothing()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;

        pool.Close(Guid.NewGuid());
        pool.Revoke(Guid.NewGuid());

        Assert.Equal(0, pool.Snapshot().Idle);
    }
}
