using Microsoft.EntityFrameworkCore;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class ContactStoreImportSyncTests
{
    [Fact]
    public async Task Importing_GivesEveryNewCardANameAndARank()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync(rank: 3);
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();

        await store.ImportAsync(userId, ContactStoreTestFactory.ImportRows(2), CancellationToken.None);

        var rows = await context.Contacts.ToListAsync(CancellationToken.None);
        Assert.All(rows, r => Assert.Equal(3ul, r.SyncSequence));
        Assert.All(rows, r => Assert.Equal($"{r.Id}.vcf", r.DavName));
    }

    [Fact]
    public async Task Importing_ArchivesTheCardsItMergesOver()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        var before = (await context.Contacts.SingleAsync(CancellationToken.None)).VCardRaw;
        sync.Invocations.Clear();

        // A row that merges onto the existing contact — same identity, one field more.
        await store.ImportAsync(
            userId, ContactStoreTestFactory.MergeRowFor("Ada", "Lovelace"), CancellationToken.None);

        sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r =>
                r.Cause == RevisionCause.Import && r.VCardRaw == before && r.ContactId == created.Value),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Importing_TakesOneRankPerBatchAndNotOnePerRow()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);

        await store.ImportAsync(
            Guid.NewGuid(), ContactStoreTestFactory.ImportRows(ContactStore.BatchSize + 5),
            CancellationToken.None);

        // Two batches, two ranks. A client syncing during the import gets the beginning rather than
        // waiting for the end — which is exactly what decision 7's rank-boundary cut can serve.
        sync.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task AnImportRowThatChangesNothing_TakesNoRankOfItsOwn()
    {
        using var context = ContactStoreTestFactory.NewContext();
        // Counting, not constant: under a constant rank the assertion below would hold even if the
        // replay did take a rank of its own, and this test would prove nothing.
        var sync = ContactStoreTestFactory.NewSyncCounting();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        await store.ImportAsync(
            userId, ContactStoreTestFactory.MergeRowFor("Ada", "Lovelace"), CancellationToken.None);
        var rankBefore = (await context.Contacts.SingleAsync(CancellationToken.None)).SyncSequence;
        sync.Invocations.Clear();

        // The same file imported twice: the second pass fills nothing in.
        await store.ImportAsync(
            userId, ContactStoreTestFactory.MergeRowFor("Ada", "Lovelace"), CancellationToken.None);

        // Re-importing the same file must not wake every phone for a book that did not change.
        var row = await context.Contacts.SingleAsync(CancellationToken.None);
        Assert.Equal(rankBefore, row.SyncSequence);
        sync.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
