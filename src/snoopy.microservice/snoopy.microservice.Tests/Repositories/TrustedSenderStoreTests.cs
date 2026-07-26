using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class TrustedSenderStoreTests
{
    private static TrustedSenderStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName));

    [Fact]
    public async Task Add_ThenList_ReturnsTheAddress()
    {
        var db = nameof(Add_ThenList_ReturnsTheAddress);
        var user = Guid.NewGuid();

        var result = await CreateStore(db).AddAsync(user, "news@example.com", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("news@example.com",
            Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None)));
    }

    // The table collates binary. Folding on the way in is the only thing stopping one sender
    // from becoming two rows, the second of which would never match anything.
    [Fact]
    public async Task Add_FoldsCaseAndSurroundingSpace()
    {
        var db = nameof(Add_FoldsCaseAndSurroundingSpace);
        var user = Guid.NewGuid();

        await CreateStore(db).AddAsync(user, "  News@Example.COM ", CancellationToken.None);

        Assert.Equal("news@example.com",
            Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None)));
    }

    [Fact]
    public async Task Add_Twice_KeepsOneRow()
    {
        var db = nameof(Add_Twice_KeepsOneRow);
        var user = Guid.NewGuid();
        await CreateStore(db).AddAsync(user, "news@example.com", CancellationToken.None);

        await CreateStore(db).AddAsync(user, "NEWS@example.com", CancellationToken.None);

        Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
    }

    // The cap is what bounds the table, not the retention sweep: a TTL deletes after the fact
    // and bounds nothing in between.
    [Fact]
    public async Task Add_AtTheCap_IsRefused()
    {
        var db = nameof(Add_AtTheCap_IsRefused);
        var user = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        for (var i = 0; i < TrustedSenderStore.MaxPerAccount; i++)
        {
            context.TrustedSenders.Add(new TrustedSender
            {
                UserId = user, Address = $"sender{i}@example.com", LastUsed = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync(CancellationToken.None);

        var result = await CreateStore(db).AddAsync(user, "one-too-many@example.com", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TrustedSenderStore.CapReached, result.Error);
    }

    // Re-approving an address already stored must not be refused by the cap: it adds no row.
    [Fact]
    public async Task Add_AtTheCap_StillAcceptsAnAddressAlreadyStored()
    {
        var db = nameof(Add_AtTheCap_StillAcceptsAnAddressAlreadyStored);
        var user = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        for (var i = 0; i < TrustedSenderStore.MaxPerAccount; i++)
        {
            context.TrustedSenders.Add(new TrustedSender
            {
                UserId = user, Address = $"sender{i}@example.com", LastUsed = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync(CancellationToken.None);

        var result = await CreateStore(db).AddAsync(user, "sender0@example.com", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Remove_DropsTheRow()
    {
        var db = nameof(Remove_DropsTheRow);
        var user = Guid.NewGuid();
        await CreateStore(db).AddAsync(user, "news@example.com", CancellationToken.None);

        await CreateStore(db).RemoveAsync(user, "NEWS@Example.com", CancellationToken.None);

        Assert.Empty(await CreateStore(db).ListAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task Remove_UnknownAddress_IsNotAnError()
    {
        var db = nameof(Remove_UnknownAddress_IsNotAnError);
        var user = Guid.NewGuid();

        await CreateStore(db).RemoveAsync(user, "stranger@example.com", CancellationToken.None);

        Assert.Empty(await CreateStore(db).ListAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task List_IsScopedToTheAccount()
    {
        var db = nameof(List_IsScopedToTheAccount);
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await CreateStore(db).AddAsync(mine, "mine@example.com", CancellationToken.None);
        await CreateStore(db).AddAsync(theirs, "theirs@example.com", CancellationToken.None);

        Assert.Equal("mine@example.com",
            Assert.Single(await CreateStore(db).ListAsync(mine, CancellationToken.None)));
    }

    [Fact]
    public async Task Touch_MovesTheDateOnAStaleRow()
    {
        var db = nameof(Touch_MovesTheDateOnAStaleRow);
        var user = Guid.NewGuid();
        var seeded = new PreferencesTestDbContext(db);
        seeded.TrustedSenders.Add(new TrustedSender
        {
            UserId = user, Address = "news@example.com", LastUsed = DateTime.UtcNow.AddDays(-40)
        });
        await seeded.SaveChangesAsync(CancellationToken.None);

        await CreateStore(db).TouchAsync(user, "News@Example.com", CancellationToken.None);

        var row = Assert.Single(new PreferencesTestDbContext(db).TrustedSenders.ToList());
        Assert.Equal(DateTime.UtcNow.Date, row.LastUsed.Date);
    }

    // The reason the touch is affordable at all: one write a day per approved sender, not one
    // per message opened. Drop this and every reopen costs an UPDATE.
    [Fact]
    public async Task Touch_TwiceInADay_WritesOnlyOnce()
    {
        var db = nameof(Touch_TwiceInADay_WritesOnlyOnce);
        var user = Guid.NewGuid();
        var seeded = new PreferencesTestDbContext(db);
        var alreadyToday = DateTime.UtcNow.Date.AddHours(1);
        seeded.TrustedSenders.Add(new TrustedSender
        {
            UserId = user, Address = "news@example.com", LastUsed = alreadyToday
        });
        await seeded.SaveChangesAsync(CancellationToken.None);

        await CreateStore(db).TouchAsync(user, "news@example.com", CancellationToken.None);

        var row = Assert.Single(new PreferencesTestDbContext(db).TrustedSenders.ToList());
        Assert.Equal(alreadyToday, row.LastUsed);
    }

    // Every message opened goes through the touch. Creating a row here would approve a sender
    // the user never chose — the opposite of the whole feature.
    [Fact]
    public async Task Touch_CreatesNothingForAnUnapprovedSender()
    {
        var db = nameof(Touch_CreatesNothingForAnUnapprovedSender);
        var user = Guid.NewGuid();

        await CreateStore(db).TouchAsync(user, "stranger@example.com", CancellationToken.None);

        Assert.Empty(await CreateStore(db).ListAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task Sweep_RemovesPastTheRetentionAndSparesInsideIt()
    {
        var db = nameof(Sweep_RemovesPastTheRetentionAndSparesInsideIt);
        var user = Guid.NewGuid();
        var seeded = new PreferencesTestDbContext(db);
        seeded.TrustedSenders.Add(new TrustedSender
        {
            UserId = user, Address = "stale@example.com", LastUsed = DateTime.UtcNow.AddDays(-400)
        });
        seeded.TrustedSenders.Add(new TrustedSender
        {
            UserId = user, Address = "fresh@example.com", LastUsed = DateTime.UtcNow.AddDays(-10)
        });
        await seeded.SaveChangesAsync(CancellationToken.None);

        var removed = await CreateStore(db).SweepExpiredAsync(TimeSpan.FromDays(365), CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.Equal("fresh@example.com",
            Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None)));
    }
}
