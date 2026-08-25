using Microsoft.EntityFrameworkCore;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class ContactStoreConflictTests
{
    [Fact]
    public async Task Updating_WithTheHashItRead_Succeeds()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var store = new ContactStore(context, ContactStoreTestFactory.NewSync().Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        var held = (await context.Contacts.SingleAsync(CancellationToken.None)).CardHash;

        var saved = await store.UpdateAsync(
            userId, created.Value,
            ContactStoreTestFactory.Write("Ada", "Byron") with { CardHash = held },
            CancellationToken.None);

        Assert.True(saved.IsSuccess);
    }

    [Fact]
    public async Task Updating_WithAStaleHash_IsRefused()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var store = new ContactStore(context, ContactStoreTestFactory.NewSync().Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);

        var saved = await store.UpdateAsync(
            userId, created.Value,
            ContactStoreTestFactory.Write("Ada", "Byron") with { CardHash = "not-the-one-it-read" },
            CancellationToken.None);

        // A tab open for ten minutes must not silently rewrite the card the phone just changed.
        Assert.True(saved.IsFailure);
        Assert.Equal(ContactStore.CardMoved, saved.Error);
    }

    [Fact]
    public async Task Updating_WithoutAHash_StillWrites()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var store = new ContactStore(context, ContactStoreTestFactory.NewSync().Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);

        var saved = await store.UpdateAsync(
            userId, created.Value, ContactStoreTestFactory.Write("Ada", "Byron"), CancellationToken.None);

        // The check is opt-in: a caller that did not read the card first is not broken by it.
        Assert.True(saved.IsSuccess);
    }

    [Fact]
    public async Task AStaleHash_IsRefusedBeforeAnyRankIsTaken()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        sync.Invocations.Clear();

        var saved = await store.UpdateAsync(
            userId, created.Value,
            ContactStoreTestFactory.Write("Ada", "Byron") with { CardHash = "stale" },
            CancellationToken.None);

        // The refusal itself, pinned here too — without it this test would still pass for a bug
        // that skipped composition for some unrelated reason and yet reported success.
        Assert.True(saved.IsFailure);
        Assert.Equal(ContactStore.CardMoved, saved.Error);

        // A refusal must open no transaction, take no lock and wake no client: the refused path is
        // the one a conflicted tab retries on every save.
        sync.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        sync.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
