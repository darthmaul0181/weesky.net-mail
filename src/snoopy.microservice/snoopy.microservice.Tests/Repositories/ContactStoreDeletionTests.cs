using Microsoft.EntityFrameworkCore;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class ContactStoreDeletionTests
{
    [Fact]
    public async Task Deleting_PlacesATombstoneAtTheNewRank()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync(rank: 12);
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        sync.Invocations.Clear();

        await store.DeleteAsync(userId, created.Value, CancellationToken.None);

        // The silent failure this closes: without a tombstone the client sees neither a change nor
        // a disappearance, keeps the card for ever, and hands it back to the user who just erased it.
        sync.Verify(s => s.PlaceTombstoneAsync(userId, $"{created.Value}.vcf", 12,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deleting_ArchivesTheCardUnderTheDeleteCause()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        var card = (await context.Contacts.SingleAsync(CancellationToken.None)).VCardRaw;
        sync.Invocations.Clear();

        await store.DeleteAsync(userId, created.Value, CancellationToken.None);

        // The tombstone does NOT carry the card, deliberately: writing the bytes in two places by
        // door would give two pruning paths, two lifetimes and two chances to repair only one.
        sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.Cause == RevisionCause.Delete && r.VCardRaw == card),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletingANamelessContact_BuriesNothingAndBreaksNothing()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var userId = Guid.NewGuid();
        var id = Guid.NewGuid();
        context.Contacts.Add(new Contact { Id = id, UserId = userId, Uid = id.ToString() });
        await context.SaveChangesAsync(CancellationToken.None);
        var store = new ContactStore(context, sync.Object);

        var outcome = await store.DeleteAsync(userId, id, CancellationToken.None);

        // No name to bury, and the row was never visible to the protocol (rank 0). The tombstone
        // key refuses NULL: this path must tolerate it, not break on it.
        Assert.True(outcome.IsSuccess);
        sync.Verify(s => s.PlaceTombstoneAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<ulong>(),
            It.IsAny<CancellationToken>()), Times.Never);
        sync.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Empty(context.Contacts);
    }

    [Fact]
    public async Task DeletingABatch_BuriesEveryRowItActuallyRemoved()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var first = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        var second = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Grace", "Hopper"), CancellationToken.None);
        sync.Invocations.Clear();

        var removed = await store.DeleteManyAsync(
            userId, [first.Value, second.Value, Guid.NewGuid()], CancellationToken.None);

        // One tombstone PER card actually removed. The bulk action bar is the busiest deletion door
        // in the product, and it is the one a per-row loop is most tempting to skip.
        Assert.Equal(2, removed);
        sync.Verify(s => s.PlaceTombstoneAsync(userId, It.IsAny<string>(), It.IsAny<ulong>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DeletingABatch_ArchivesEveryCardItRemoved()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var first = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        var second = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Grace", "Hopper"), CancellationToken.None);
        sync.Invocations.Clear();

        await store.DeleteManyAsync(userId, [first.Value, second.Value], CancellationToken.None);

        sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.Cause == RevisionCause.Delete), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task DeletingABatch_TakesOneRankPerTransactionAndNotOnePerCard()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var created = await store.CreateAsync(
                userId, ContactStoreTestFactory.Write($"First{i}", "Last"), CancellationToken.None);
            ids.Add(created.Value);
        }
        sync.Invocations.Clear();

        await store.DeleteManyAsync(userId, ids, CancellationToken.None);

        // One transaction, one rank: the state row is locked once, and incrementing it further
        // would distinguish nothing since everything becomes visible at the same COMMIT. Three
        // cards well under the batch size means exactly one rank.
        sync.Verify(s => s.NextSequenceAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletingMoreThanABatch_TakesOneRankPerBatch()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var ids = new List<Guid>();
        for (var i = 0; i < ContactStore.BatchSize + 5; i++)
        {
            var created = await store.CreateAsync(
                userId, ContactStoreTestFactory.Write($"First{i}", "Last"), CancellationToken.None);
            ids.Add(created.Value);
        }
        sync.Invocations.Clear();

        await store.DeleteManyAsync(userId, ids, CancellationToken.None);

        // "One transaction, one rank" holds; "one bulk action, one rank" does not, and nothing
        // asked for it. Several ranks for one bulk deletion are exactly what a client syncing
        // during it can serve, rank by rank, rather than waiting for the end.
        sync.Verify(s => s.NextSequenceAsync(userId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
