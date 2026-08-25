using Microsoft.EntityFrameworkCore;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class ContactStoreSyncTests
{
    [Fact]
    public async Task Creating_TakesARankAndNamesTheResource()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync(rank: 4);
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();

        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);

        var row = await context.Contacts.SingleAsync(CancellationToken.None);
        Assert.Equal(4ul, row.SyncSequence);
        // {id}.vcf is what clients show in their logs; there is no reason to puzzle them.
        Assert.Equal($"{created.Value}.vcf", row.DavName);
    }

    [Fact]
    public async Task Creating_LiftsATombstoneOfTheSameName()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();

        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);

        // A name that comes back must stop being reported as deleted, or a client that syncs after
        // both events sees a creation and a burial at the same rank and picks whichever it likes.
        sync.Verify(s => s.LiftTombstoneAsync(userId, $"{created.Value}.vcf", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Creating_ArchivesNothing()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);

        await store.CreateAsync(
            Guid.NewGuid(), ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);

        // There is nothing to archive: no card was replaced.
        sync.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Updating_ArchivesTheBytesItReplaces_BeforeTakingItsRank()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync(rank: 9);
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        var before = (await context.Contacts.SingleAsync(CancellationToken.None)).VCardRaw;
        sync.Invocations.Clear();

        await store.UpdateAsync(
            userId, created.Value, ContactStoreTestFactory.Write("Ada", "Byron"), CancellationToken.None);

        sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r =>
                r.Cause == RevisionCause.Webmail
                && r.VCardRaw == before
                && r.ContactId == created.Value),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Updating_TakesTheNewRank()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync(rank: 9);
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);

        await store.UpdateAsync(
            userId, created.Value, ContactStoreTestFactory.Write("Ada", "Byron"), CancellationToken.None);

        var row = await context.Contacts.SingleAsync(CancellationToken.None);
        Assert.Equal(9ul, row.SyncSequence);
    }

    [Fact]
    public async Task AWriteThatChangesNothing_TakesNoRankAndArchivesNothing()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var write = ContactStoreTestFactory.Write("Ada", "Lovelace");
        var created = await store.CreateAsync(userId, write, CancellationToken.None);
        var rankAfterCreate = (await context.Contacts.SingleAsync(CancellationToken.None)).SyncSequence;
        sync.Invocations.Clear();

        await store.UpdateAsync(userId, created.Value, write, CancellationToken.None);

        // The editor reopened and closed again. The sequence advances exactly when card_hash
        // changes: waking every phone for a write that changed nothing is the failure this guards.
        sync.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        sync.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(rankAfterCreate, (await context.Contacts.SingleAsync(CancellationToken.None)).SyncSequence);
    }

    [Fact]
    public async Task TogglingTheStar_IsInvisibleToTheProtocol()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        sync.Invocations.Clear();

        await store.SetFavoriteAsync(userId, created.Value, true, CancellationToken.None);

        // is_favorite is projected from nothing and must not be visible to the protocol either.
        // This is the trap decision 6 answers in one sentence, and the one nobody thinks to check.
        sync.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TogglingTheStarOverABatch_IsInvisibleToo()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        sync.Invocations.Clear();

        await store.SetFavoriteManyAsync(userId, [created.Value], true, CancellationToken.None);

        sync.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AContactWithNoCard_IsUpdatedWithoutArchivingAnything()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var userId = Guid.NewGuid();
        var id = Guid.NewGuid();
        // The shape the 4a backfill has not reached: no card, no hash, no name.
        context.Contacts.Add(new Contact { Id = id, UserId = userId, Uid = id.ToString() });
        await context.SaveChangesAsync(CancellationToken.None);
        var store = new ContactStore(context, sync.Object);

        await store.UpdateAsync(
            userId, id, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);

        // No card, no revision. The write path tolerates it rather than breaking on it — and it
        // gives the row the name it lacked, in the same transaction, so a webmail edit during the
        // backfill window cannot create a row with a rank above zero and no name.
        sync.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()), Times.Never);
        var row = await context.Contacts.SingleAsync(CancellationToken.None);
        Assert.Equal($"{id}.vcf", row.DavName);
        Assert.NotEqual(0ul, row.SyncSequence);
    }
}
