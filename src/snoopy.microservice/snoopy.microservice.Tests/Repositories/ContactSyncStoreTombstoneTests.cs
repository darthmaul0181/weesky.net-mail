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

        // The lift removes the row, so this path alone would not catch a bare INSERT — what this
        // pins is that the rank refreshes to the newer burial after a full lift-then-place cycle.
        var stone = Assert.Single(new PreferencesTestDbContext(db).ContactTombstones);
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

        // No lift between the two places: the row from the first burial is still there, so this is
        // the case a bare INSERT would fail on a duplicate key — in production, on data the user
        // believes gone.
        var stone = Assert.Single(new PreferencesTestDbContext(db).ContactTombstones);
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
    public async Task Pruning_SetsTheWatermarkToTheHighestPrunedRank()
    {
        var db = nameof(Pruning_SetsTheWatermarkToTheHighestPrunedRank);
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

        // GREATEST, never the plain assignment. Two concurrent sweeps would collide loudly instead
        // of silently — RemoveRange throws when the second one to commit finds its rows already
        // gone — so this is not exercising that path; it only pins that a single sweep never lowers
        // an existing watermark on its own.
        var state = await store.ReadStateAsync(userId, CancellationToken.None);
        Assert.Equal(30ul, state!.PrunedBelow);
    }

    [Fact]
    public async Task Pruning_GivesEachUserTheirOwnMaxAndNotAnotherUsersMax()
    {
        var db = nameof(Pruning_GivesEachUserTheirOwnMaxAndNotAnotherUsersMax);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var context = new PreferencesTestDbContext(db);
        context.ContactSyncStates.Add(new ContactSyncState
        {
            UserId = userA, Epoch = Guid.NewGuid(), Seq = 20, PrunedBelow = 0
        });
        context.ContactSyncStates.Add(new ContactSyncState
        {
            UserId = userB, Epoch = Guid.NewGuid(), Seq = 50, PrunedBelow = 0
        });
        context.ContactTombstones.Add(new ContactTombstone
        {
            UserId = userA, DavName = "a.vcf", SyncSequence = 4, DeletedAt = now.AddDays(-200)
        });
        context.ContactTombstones.Add(new ContactTombstone
        {
            UserId = userB, DavName = "b.vcf", SyncSequence = 45, DeletedAt = now.AddDays(-200)
        });
        await context.SaveChangesAsync(CancellationToken.None);
        var store = new ContactSyncStore(context);

        await store.PruneAsync(now.AddDays(-180), now.AddDays(-30), CancellationToken.None);

        // A single global max stamped onto every row would pass every other prune test here, since
        // they all use one user. Two users, two different ranks, is the only way to pin the
        // per-user grouping rather than a max taken across the whole doomed set.
        var stateA = await store.ReadStateAsync(userA, CancellationToken.None);
        var stateB = await store.ReadStateAsync(userB, CancellationToken.None);
        Assert.Equal(4ul, stateA!.PrunedBelow);
        Assert.Equal(45ul, stateB!.PrunedBelow);
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
