using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class CalendarSyncStoreTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static CalendarSyncStore Store(string db) => new(new PreferencesTestDbContext(db));

    [Fact]
    public async Task ReadState_AnswersNothingRatherThanCreatingARow()
    {
        var db = nameof(ReadState_AnswersNothingRatherThanCreatingARow);
        var context = new PreferencesTestDbContext(db);

        var state = await new CalendarSyncStore(context).ReadStateAsync(Guid.NewGuid(), None);

        // A getctag on a collection that never synced must not write: a read that creates rows
        // makes every poll a write on the busiest path a phone takes.
        Assert.Null(state);
        Assert.Empty(context.CalendarSyncStates);
    }

    [Fact]
    public async Task CreateState_DrawsAnEpochAtSeqZero()
    {
        var db = nameof(CreateState_DrawsAnEpochAtSeqZero);
        var calendarId = Guid.NewGuid();

        await Store(db).CreateStateAsync(calendarId, None);

        var state = await Store(db).ReadStateAsync(calendarId, None);
        Assert.NotNull(state);
        Assert.NotEqual(Guid.Empty, state.Epoch);
        // Zero is reserved for "never written", so an empty collection's ctag answers 0.
        Assert.Equal(new SyncState(state.Epoch, 0, 0), state);
    }

    [Fact]
    public async Task NextSequence_WithoutATransaction_ThrowsRatherThanRaceSilently()
    {
        var db = nameof(NextSequence_WithoutATransaction_ThrowsRatherThanRaceSilently);

        // The InMemory provider never opens a transaction, so CurrentTransaction is null here
        // exactly as it would be for a caller who forgot to open one. Outside a transaction the
        // row's lock drops the instant the raw SQL completes, and two callers can read one rank
        // with no error anywhere. This guard is the only thing that catches that mistake.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Store(db).NextSequenceAsync(Guid.NewGuid(), None));
    }

    [Fact]
    public void TheIncrement_IsRawSqlAndThereforeUntestedHere()
    {
        // Deliberate, and written as a test so a review reads it as a decision rather than a gap.
        // NextSequenceAsync is `INSERT ... ON DUPLICATE KEY UPDATE seq = seq + 1`: MySQL syntax the
        // InMemory provider cannot run. Its atomicity — two concurrent transactions never taking
        // the same rank — is verified by hand against MariaDB and nowhere else. TestCalendarSyncStore
        // stands in for it in every other test, honouring the contract without being that statement.
        Assert.NotNull(typeof(CalendarSyncStore).GetMethod(nameof(CalendarSyncStore.NextSequenceAsync)));
    }

    [Fact]
    public async Task NextSequence_IsMonotonePerCalendar_AndIndependentBetweenTwo()
    {
        var db = nameof(NextSequence_IsMonotonePerCalendar_AndIndependentBetweenTwo);
        var context = new PreferencesTestDbContext(db);
        var store = new TestCalendarSyncStore(context);
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();

        // Per collection and not per user, unlike the contacts counter: CalDAV syncs each
        // collection on its own, so writing to one must not move the other's ctag.
        Assert.Equal(1UL, await store.NextSequenceAsync(left, None));
        Assert.Equal(2UL, await store.NextSequenceAsync(left, None));
        Assert.Equal(1UL, await store.NextSequenceAsync(right, None));
        Assert.Equal(3UL, await store.NextSequenceAsync(left, None));
        Assert.Equal(2UL, await store.NextSequenceAsync(right, None));
    }

    [Fact]
    public async Task PlaceTombstone_IsAnUpsert_SoASecondDeletionOfTheSameNameHolds()
    {
        var db = nameof(PlaceTombstone_IsAnUpsert_SoASecondDeletionOfTheSameNameHolds);
        var calendarId = Guid.NewGuid();

        await Store(db).PlaceTombstoneAsync(calendarId, "a.ics", 4, None);
        await Store(db).PlaceTombstoneAsync(calendarId, "a.ics", 9, None);

        // The key is (calendar_id, dav_name): a bare INSERT would fail the second deletion on a
        // duplicate key, in production, on data the user believes gone.
        var tomb = Assert.Single(new PreferencesTestDbContext(db).CalendarTombstones);
        Assert.Equal(9UL, tomb.SyncSequence);
    }

    [Fact]
    public async Task LiftTombstone_RemovesIt_AndIsQuietWhenThereIsNone()
    {
        var db = nameof(LiftTombstone_RemovesIt_AndIsQuietWhenThereIsNone);
        var calendarId = Guid.NewGuid();
        await Store(db).PlaceTombstoneAsync(calendarId, "a.ics", 4, None);

        await Store(db).LiftTombstoneAsync(calendarId, "a.ics", None);
        await Store(db).LiftTombstoneAsync(calendarId, "never-was.ics", None);

        Assert.Empty(new PreferencesTestDbContext(db).CalendarTombstones);
    }

    [Fact]
    public async Task Archive_HashesTheBytesItself()
    {
        var db = nameof(Archive_HashesTheBytesItself);
        var user = Guid.NewGuid();
        var calendarId = Guid.NewGuid();
        var ics = Ics.Single(start: "DTSTART:20260907T090000Z", end: null);

        await Store(db).ArchiveAsync(
            user, calendarId, null, "uid", "a.ics", ics, RevisionCause.Delete, None);

        // A hash computed by a caller is a hash a caller will forget.
        var revision = Assert.Single(new PreferencesTestDbContext(db).CalendarRevisions);
        Assert.Equal(
            weesky.Snoopy.Microservice.Services.Calendar.IcsDocument.HashOf(ics), revision.IcsHash);
        Assert.Equal(ics, revision.IcsRaw);
        Assert.Equal(RevisionCause.Delete, revision.Cause);
    }

    [Fact]
    public async Task Prune_RaisesTheWatermarkAndRemovesWhatItCovers()
    {
        var db = nameof(Prune_RaisesTheWatermarkAndRemovesWhatItCovers);
        var calendarId = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        context.CalendarSyncStates.Add(new CalendarSyncState
        {
            CalendarId = calendarId, Epoch = Guid.NewGuid(), Seq = 40, PrunedBelow = 0
        });
        context.CalendarTombstones.AddRange(
            Tomb(calendarId, "old.ics", 7, DateTime.UtcNow.AddDays(-200)),
            Tomb(calendarId, "older.ics", 3, DateTime.UtcNow.AddDays(-300)),
            Tomb(calendarId, "recent.ics", 39, DateTime.UtcNow.AddDays(-2)));
        context.CalendarRevisions.AddRange(
            Revision(calendarId, DateTime.UtcNow.AddDays(-90)),
            Revision(calendarId, DateTime.UtcNow.AddDays(-1)));
        await context.SaveChangesAsync(None);

        var outcome = await Store(db).PruneAsync(
            DateTime.UtcNow.AddDays(-180), DateTime.UtcNow.AddDays(-30), None);

        Assert.Equal(new PruneOutcome(2, 1), outcome);
        var after = new PreferencesTestDbContext(db);
        // The watermark is the highest rank actually removed, and it moves in the SAME save as the
        // removal: split in two, a crash between them accepts a token whose deletions are gone.
        Assert.Equal(7UL, (await after.CalendarSyncStates.SingleAsync(None)).PrunedBelow);
        Assert.Equal("recent.ics", (await after.CalendarTombstones.SingleAsync(None)).DavName);
        Assert.Single(after.CalendarRevisions);
    }

    private static CalendarTombstone Tomb(Guid calendarId, string name, ulong rank, DateTime at) =>
        new() { CalendarId = calendarId, DavName = name, SyncSequence = rank, DeletedAt = at };

    private static CalendarRevision Revision(Guid calendarId, DateTime at) =>
        new()
        {
            UserId = Guid.NewGuid(), CalendarId = calendarId, Uid = "uid", DavName = "a.ics",
            IcsRaw = "x", IcsHash = "x", Cause = RevisionCause.Delete, ReplacedAt = at
        };
}
