using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class ContactSyncStoreTests
{
    private static ContactSyncStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName));

    [Fact]
    public async Task ReadState_AnswersNothingRatherThanCreatingARow()
    {
        var db = nameof(ReadState_AnswersNothingRatherThanCreatingARow);
        var context = new PreferencesTestDbContext(db);
        var store = new ContactSyncStore(context);

        var state = await store.ReadStateAsync(Guid.NewGuid(), CancellationToken.None);

        // A getctag on a book that has never synced must not write: an empty book answers 0, and a
        // read that creates rows makes every poll a write on the busiest path a phone takes.
        Assert.Null(state);
        Assert.Empty(context.ContactSyncStates);
    }

    [Fact]
    public async Task ReadState_AnswersTheThreeNumbersTogether()
    {
        var db = nameof(ReadState_AnswersTheThreeNumbersTogether);
        var userId = Guid.NewGuid();
        var epoch = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        context.ContactSyncStates.Add(new ContactSyncState
        {
            UserId = userId, Epoch = epoch, Seq = 42, PrunedBelow = 7
        });
        await context.SaveChangesAsync(CancellationToken.None);

        var state = await CreateStore(db).ReadStateAsync(userId, CancellationToken.None);

        Assert.Equal(new SyncState(epoch, 42, 7), state);
    }

    [Fact]
    public async Task ReadOrCreate_DrawsAnEpochOnTheFirstCall()
    {
        var db = nameof(ReadOrCreate_DrawsAnEpochOnTheFirstCall);
        var userId = Guid.NewGuid();

        var state = await CreateStore(db).ReadOrCreateStateAsync(userId, CancellationToken.None);

        // sync-collection on an empty book needs an epoch to form its token, so this one creates.
        Assert.NotEqual(Guid.Empty, state.Epoch);
        Assert.Equal(0ul, state.Seq);
        Assert.Equal(0ul, state.PrunedBelow);
        // The returned record alone does not pin that anything was written — a pure "new
        // SyncState(Guid.NewGuid(), 0, 0)" with no store call would satisfy the three asserts above.
        Assert.Single(new PreferencesTestDbContext(db).ContactSyncStates);
    }

    [Fact]
    public async Task ReadOrCreate_KeepsTheEpochItAlreadyDrew()
    {
        var db = nameof(ReadOrCreate_KeepsTheEpochItAlreadyDrew);
        var userId = Guid.NewGuid();

        var first = await CreateStore(db).ReadOrCreateStateAsync(userId, CancellationToken.None);
        var second = await CreateStore(db).ReadOrCreateStateAsync(userId, CancellationToken.None);

        // The epoch is what makes a token belong to this book. Redrawing it on a second read would
        // silently invalidate every client's token on every poll.
        Assert.Equal(first.Epoch, second.Epoch);
        Assert.Single(new PreferencesTestDbContext(db).ContactSyncStates);
    }

    [Fact]
    public async Task NextSequence_WithoutATransaction_ThrowsRatherThanRaceSilently()
    {
        var db = nameof(NextSequence_WithoutATransaction_ThrowsRatherThanRaceSilently);
        var store = CreateStore(db);

        // The InMemory provider never opens a transaction, so CurrentTransaction is null here
        // exactly as it would be for a caller in tasks 5-9 who forgot to open one. Outside a
        // transaction the row's lock drops the instant the raw SQL below completes, and the
        // re-read can land on a different pooled connection — two callers then read the same rank
        // with no error anywhere. This guard is the only thing that can catch that mistake.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.NextSequenceAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public void TheIncrement_IsRawSqlAndThereforeUntestedHere()
    {
        // Deliberate, and written as a test so a review reads it as a decision rather than a gap.
        // NextSequenceAsync is `INSERT ... ON DUPLICATE KEY UPDATE seq = seq + 1`: MySQL syntax the
        // InMemory provider cannot run and SQLite spells differently. Its atomicity — two
        // concurrent transactions never taking the same rank — is verified by hand against
        // MariaDB, once, by the procedure in Task 3 Step 6, and nowhere else. Writing an EF variant
        // "for the tests" would give two paths for one invariant, which is how one comes to believe
        // an untested thing is tested.
        var method = typeof(ContactSyncStore).GetMethod(nameof(ContactSyncStore.NextSequenceAsync));

        Assert.NotNull(method);
    }
}
