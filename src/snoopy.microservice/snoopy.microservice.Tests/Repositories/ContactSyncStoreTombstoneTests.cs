using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class ContactSyncStoreTombstoneTests
{
    private static ContactRevision Revision(
        Guid userId, string? davName, string hash, RevisionCause cause, DateTime at) =>
        new()
        {
            UserId = userId, ContactId = Guid.NewGuid(), Uid = "uid-1", DavName = davName,
            CardHash = hash, VCardRaw = "BEGIN:VCARD\r\nEND:VCARD\r\n", Cause = cause,
            ReplacedAt = at
        };

    [Fact]
    public async Task ATombstone_IsWrittenOnce()
    {
        var db = nameof(ATombstone_IsWrittenOnce);
        var userId = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        var store = new ContactSyncStore(context);

        await store.PlaceTombstoneAsync(userId, "a.vcf", 5, CancellationToken.None);

        var stone = Assert.Single(context.ContactTombstones);
        Assert.Equal("a.vcf", stone.DavName);
        Assert.Equal(5ul, stone.SyncSequence);
    }

    [Fact]
    public async Task ANameDeletedTwice_KeepsOneTombstoneAtTheNewerRank()
    {
        var db = nameof(ANameDeletedTwice_KeepsOneTombstoneAtTheNewerRank);
        var userId = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        var store = new ContactSyncStore(context);

        await store.PlaceTombstoneAsync(userId, "a.vcf", 5, CancellationToken.None);
        await store.LiftTombstoneAsync(userId, "a.vcf", CancellationToken.None);
        await store.PlaceTombstoneAsync(userId, "a.vcf", 9, CancellationToken.None);

        // Deleted, recreated, deleted again lands on the same key. A bare INSERT would fail the
        // second deletion on a duplicate key — in production, on data the user believes gone.
        var stone = Assert.Single(context.ContactTombstones);
        Assert.Equal(9ul, stone.SyncSequence);
    }

    [Fact]
    public async Task ATombstoneReplaced_WithoutBeingLifted_TakesTheNewerRankToo()
    {
        var db = nameof(ATombstoneReplaced_WithoutBeingLifted_TakesTheNewerRankToo);
        var userId = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        var store = new ContactSyncStore(context);

        await store.PlaceTombstoneAsync(userId, "a.vcf", 5, CancellationToken.None);
        await store.PlaceTombstoneAsync(userId, "a.vcf", 9, CancellationToken.None);

        // The same path without the lift: recreation through a door that forgot to lift must not
        // turn the second burial into a crash either.
        var stone = Assert.Single(context.ContactTombstones);
        Assert.Equal(9ul, stone.SyncSequence);
    }

    [Fact]
    public async Task LiftingATombstoneThatIsNotThere_IsQuiet()
    {
        var db = nameof(LiftingATombstoneThatIsNotThere_IsQuiet);
        var context = new PreferencesTestDbContext(db);
        var store = new ContactSyncStore(context);

        await store.LiftTombstoneAsync(Guid.NewGuid(), "never-buried.vcf", CancellationToken.None);

        // Every create lifts, and most names were never buried. Throwing here would make the
        // ordinary path carry a try/catch.
        Assert.Empty(context.ContactTombstones);
    }

    [Fact]
    public async Task ARevision_IsArchived()
    {
        var db = nameof(ARevision_IsArchived);
        var userId = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        var store = new ContactSyncStore(context);

        var archived = await store.ArchiveAsync(
            Revision(userId, "a.vcf", "h1", RevisionCause.Put, DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(archived);
        Assert.Single(context.ContactRevisions);
    }

    [Fact]
    public async Task TheSameRejectedBody_WithinTwentyFourHours_IsArchivedOnce()
    {
        var db = nameof(TheSameRejectedBody_WithinTwentyFourHours_IsArchivedOnce);
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var context = new PreferencesTestDbContext(db);
        var store = new ContactSyncStore(context);

        await store.ArchiveAsync(
            Revision(userId, "a.vcf", "h1", RevisionCause.Rejected, now.AddHours(-1)),
            CancellationToken.None);
        var second = await store.ArchiveAsync(
            Revision(userId, "a.vcf", "h1", RevisionCause.Rejected, now),
            CancellationToken.None);

        // A client in disagreement is not in disagreement once but on every cycle: a phone replaying
        // the same card every quarter hour writes one revision, not ninety-six.
        Assert.False(second);
        Assert.Single(context.ContactRevisions);
    }

    [Fact]
    public async Task TheSameRejectedBody_OnTwoNames_IsArchivedTwice()
    {
        var db = nameof(TheSameRejectedBody_OnTwoNames_IsArchivedTwice);
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var context = new PreferencesTestDbContext(db);
        var store = new ContactSyncStore(context);

        await store.ArchiveAsync(
            Revision(userId, "a.vcf", "h1", RevisionCause.Rejected, now.AddHours(-1)),
            CancellationToken.None);
        var second = await store.ArchiveAsync(
            Revision(userId, "b.vcf", "h1", RevisionCause.Rejected, now),
            CancellationToken.None);

        // The name is part of the key: the same body refused on two names is two facts.
        Assert.True(second);
        Assert.Equal(2, context.ContactRevisions.Count());
    }

    [Fact]
    public async Task TheSameRejectedBody_BeyondTwentyFourHours_IsArchivedAgain()
    {
        var db = nameof(TheSameRejectedBody_BeyondTwentyFourHours_IsArchivedAgain);
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var context = new PreferencesTestDbContext(db);
        var store = new ContactSyncStore(context);

        await store.ArchiveAsync(
            Revision(userId, "a.vcf", "h1", RevisionCause.Rejected, now.AddHours(-25)),
            CancellationToken.None);
        var second = await store.ArchiveAsync(
            Revision(userId, "a.vcf", "h1", RevisionCause.Rejected, now),
            CancellationToken.None);

        Assert.True(second);
        Assert.Equal(2, context.ContactRevisions.Count());
    }

    [Fact]
    public async Task TheDeduplicationWindow_DoesNotApplyToAnAcceptedWrite()
    {
        var db = nameof(TheDeduplicationWindow_DoesNotApplyToAnAcceptedWrite);
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var context = new PreferencesTestDbContext(db);
        var store = new ContactSyncStore(context);

        await store.ArchiveAsync(
            Revision(userId, "a.vcf", "h1", RevisionCause.Put, now.AddHours(-1)),
            CancellationToken.None);
        var second = await store.ArchiveAsync(
            Revision(userId, "a.vcf", "h1", RevisionCause.Put, now),
            CancellationToken.None);

        // The window exists for a client looping on a refusal. Two accepted writes that happen to
        // land on the same hash are two facts, and dropping the second would lose an overwrite.
        Assert.True(second);
        Assert.Equal(2, context.ContactRevisions.Count());
    }

    [Fact]
    public async Task Pruning_RaisesTheWatermarkBeforeItRemovesAnything()
    {
        var db = nameof(Pruning_RaisesTheWatermarkBeforeItRemovesAnything);
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var context = new PreferencesTestDbContext(db);
        context.ContactSyncStates.Add(new ContactSyncState
        {
            UserId = userId, Epoch = Guid.NewGuid(), Seq = 20, PrunedBelow = 0
        });
        context.ContactTombstones.Add(new ContactTombstone
        {
            UserId = userId, DavName = "old.vcf", SyncSequence = 4, DeletedAt = now.AddDays(-200)
        });
        context.ContactTombstones.Add(new ContactTombstone
        {
            UserId = userId, DavName = "recent.vcf", SyncSequence = 12, DeletedAt = now.AddDays(-2)
        });
        await context.SaveChangesAsync(CancellationToken.None);
        var store = new ContactSyncStore(context);

        var outcome = await store.PruneAsync(now.AddDays(-180), now.AddDays(-30), CancellationToken.None);

        Assert.Equal(1, outcome.Tombstones);
        // The watermark is the highest rank pruned, so a token at or below 4 is now unrecoverable
        // and must answer 403 rather than silently omitting the deletion it can no longer describe.
        var state = await store.ReadStateAsync(userId, CancellationToken.None);
        Assert.Equal(4ul, state!.PrunedBelow);
        Assert.Single(context.ContactTombstones);
    }

    [Fact]
    public async Task Pruning_NeverLowersTheWatermark()
    {
        var db = nameof(Pruning_NeverLowersTheWatermark);
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var context = new PreferencesTestDbContext(db);
        context.ContactSyncStates.Add(new ContactSyncState
        {
            UserId = userId, Epoch = Guid.NewGuid(), Seq = 40, PrunedBelow = 30
        });
        context.ContactTombstones.Add(new ContactTombstone
        {
            UserId = userId, DavName = "old.vcf", SyncSequence = 4, DeletedAt = now.AddDays(-200)
        });
        await context.SaveChangesAsync(CancellationToken.None);
        var store = new ContactSyncStore(context);

        await store.PruneAsync(now.AddDays(-180), now.AddDays(-30), CancellationToken.None);

        // GREATEST, and it is what makes the sweep safe on several instances at once: the write is
        // commutative, and a DELETE that no longer finds its rows is a DELETE of zero rows.
        var state = await store.ReadStateAsync(userId, CancellationToken.None);
        Assert.Equal(30ul, state!.PrunedBelow);
    }

    [Fact]
    public async Task Pruning_WithNothingToRemove_LeavesTheWatermarkAlone()
    {
        var db = nameof(Pruning_WithNothingToRemove_LeavesTheWatermarkAlone);
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var context = new PreferencesTestDbContext(db);
        context.ContactSyncStates.Add(new ContactSyncState
        {
            UserId = userId, Epoch = Guid.NewGuid(), Seq = 8, PrunedBelow = 0
        });
        await context.SaveChangesAsync(CancellationToken.None);
        var store = new ContactSyncStore(context);

        var outcome = await store.PruneAsync(now.AddDays(-180), now.AddDays(-30), CancellationToken.None);

        Assert.Equal(new PruneOutcome(0, 0), outcome);
        var state = await store.ReadStateAsync(userId, CancellationToken.None);
        Assert.Equal(0ul, state!.PrunedBelow);
    }

    [Fact]
    public async Task Pruning_TakesRevisionsOnTheirOwnClock()
    {
        var db = nameof(Pruning_TakesRevisionsOnTheirOwnClock);
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var context = new PreferencesTestDbContext(db);
        context.ContactRevisions.Add(Revision(userId, "a.vcf", "h1", RevisionCause.Put, now.AddDays(-40)));
        context.ContactRevisions.Add(Revision(userId, "b.vcf", "h2", RevisionCause.Put, now.AddDays(-10)));
        await context.SaveChangesAsync(CancellationToken.None);
        var store = new ContactSyncStore(context);

        var outcome = await store.PruneAsync(now.AddDays(-180), now.AddDays(-30), CancellationToken.None);

        // Thirty days and not a hundred and eighty, and the asymmetry is meant: the tombstone is
        // what the PROTOCOL must still be able to tell a client gone a long time, the revision is
        // what a HUMAN might still want back.
        Assert.Equal(1, outcome.Revisions);
        Assert.Single(context.ContactRevisions);
    }
}
