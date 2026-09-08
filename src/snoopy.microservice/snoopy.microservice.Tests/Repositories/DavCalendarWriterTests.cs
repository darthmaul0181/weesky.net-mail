using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using CalendarRow = weesky.Snoopy.Microservice.Data.Preferences.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class DavCalendarWriterTests : IAsyncLifetime
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly string database = Guid.NewGuid().ToString();
    private PreferencesTestDbContext context = null!;
    private TestCalendarSyncStore sync = null!;
    private DavCalendarWriter writer = null!;
    private Guid userId;
    private Guid calendarId;

    public async Task InitializeAsync()
    {
        (_, userId, calendarId) = await CalendarStoreTestFactory.SeedAsync(database);
        context = new PreferencesTestDbContext(database);
        sync = new TestCalendarSyncStore(context);
        writer = NewWriter(context, sync);
    }

    public Task DisposeAsync()
    {
        context.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task PuttingANewName_CreatesVerbatimAndProjects()
    {
        var ics = Event("u1", "Standup");

        var outcome = await writer.PutAsync(userId, calendarId, "a.ics", ics, None);

        Assert.Equal(DavWriteStatus.Created, outcome.Status);
        Assert.Equal(1ul, outcome.Sequence);
        var row = await RowOf("a.ics");
        // The name is the URL's, never a fabricated {id}.ics; the bytes are the sent ones, and the
        // index is projected from them — the row is a resource AND a searchable event.
        Assert.Equal("a.ics", row.DavName);
        Assert.Equal(ics, row.IcsRaw);
        Assert.Equal("u1", row.Uid);
        Assert.Equal("Standup", row.Summary);
        Assert.Equal(new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc), row.StartsAt);
        Assert.Equal(1ul, row.SyncSequence);
        Assert.Equal($"\"{row.IcsHash}\"", outcome.Etag);
    }

    [Fact]
    public async Task TheBytesAreStoredAsTheyArrive_NoTimeZoneNoStampNoUidAdded()
    {
        const string lfOnly = "BEGIN:VCALENDAR\nVERSION:2.0\nPRODID:x\nBEGIN:VEVENT\nUID:u1\nDTSTART:20260907T090000Z\nEND:VEVENT\nEND:VCALENDAR\n";

        var outcome = await writer.PutAsync(userId, calendarId, "a.ics", lfOnly, None);

        // Verbatim is what makes the ETag honest: the tag describes exactly what a GET serves.
        Assert.Equal(DavWriteStatus.Created, outcome.Status);
        Assert.Equal(lfOnly, (await RowOf("a.ics")).IcsRaw);
        Assert.Equal($"\"{IcsDocument.HashOf(lfOnly)}\"", outcome.Etag);
    }

    [Fact]
    public async Task PuttingOverAnExistingName_ReplacesArchivesAndAdvancesTheRank()
    {
        var first = Event("u1", "Ada");
        await writer.PutAsync(userId, calendarId, "a.ics", first, None);

        var outcome = await writer.PutAsync(userId, calendarId, "a.ics", Event("u1", "Grace"), None);

        Assert.Equal(DavWriteStatus.Replaced, outcome.Status);
        Assert.Equal(2ul, outcome.Sequence);
        Assert.NotNull(outcome.Etag);
        // The replaced bytes, under the Put cause — not the incoming ones.
        var revision = Assert.Single(context.CalendarRevisions);
        Assert.Equal(RevisionCause.Put, revision.Cause);
        Assert.Equal(first, revision.IcsRaw);
        Assert.Equal("Grace", (await RowOf("a.ics")).Summary);
    }

    [Fact]
    public async Task AByteIdenticalRePut_TakesNoRankAndKeepsItsEtag()
    {
        var ics = Event("u1", "Ada");
        var first = await writer.PutAsync(userId, calendarId, "a.ics", ics, None);
        var ranks = sync.RankCalls;

        var second = await writer.PutAsync(userId, calendarId, "a.ics", ics, None);

        // The idempotent retry every DAV client makes: nothing changes, so no rank and no client
        // woken over a write that moved nothing.
        Assert.Equal(DavWriteStatus.Replaced, second.Status);
        Assert.Equal(first.Etag, second.Etag);
        Assert.Equal(ranks, sync.RankCalls);
        Assert.Empty(context.CalendarRevisions);
    }

    [Fact]
    public async Task AStaleIfMatch_IsRefusedWithoutARank()
    {
        await writer.PutAsync(userId, calendarId, "a.ics", Event("u1", "Ada"), None);
        var ranks = sync.RankCalls;

        var outcome = await writer.PutAsync(userId, calendarId, "a.ics", Event("u1", "Grace"), None,
            ifMatch: "\"stale\"");

        Assert.Equal(DavWriteStatus.PreconditionFailed, outcome.Status);
        Assert.Equal(ranks, sync.RankCalls);
        Assert.Equal("Ada", (await RowOf("a.ics")).Summary);
    }

    [Fact]
    public async Task AConditionalPut_WithTheTagItRead_Replaces()
    {
        var created = await writer.PutAsync(userId, calendarId, "a.ics", Event("u1", "Ada"), None);

        var outcome = await writer.PutAsync(userId, calendarId, "a.ics", Event("u1", "Grace"), None,
            ifMatch: created.Etag);

        Assert.Equal(DavWriteStatus.Replaced, outcome.Status);
    }

    [Fact]
    public async Task AConditionalPut_OnAnAbsentName_IsRefused()
    {
        var outcome = await writer.PutAsync(userId, calendarId, "never.ics", Event("u1", "Ada"), None,
            ifMatch: "\"x\"");

        // No current representation, so If-Match fails whatever it lists — including *.
        Assert.Equal(DavWriteStatus.PreconditionFailed, outcome.Status);
    }

    [Fact]
    public async Task ACreateOnlyPut_OverAnExistingName_IsRefusedBeforeAnything()
    {
        await writer.PutAsync(userId, calendarId, "a.ics", Event("u1", "Winner"), None);
        var ranks = sync.RankCalls;

        var outcome = await writer.PutAsync(userId, calendarId, "a.ics", Event("u1", "Loser"), None,
            createOnly: true);

        Assert.Equal(DavWriteStatus.AlreadyExists, outcome.Status);
        Assert.Equal(ranks, sync.RankCalls);
        Assert.Equal("Winner", (await RowOf("a.ics")).Summary);
    }

    [Fact]
    public async Task ACreateOnlyPut_OnAFreeName_Creates() =>
        Assert.Equal(DavWriteStatus.Created, (await writer.PutAsync(
            userId, calendarId, "a.ics", Event("u1", "Ada"), None, createOnly: true)).Status);

    [Fact]
    public async Task AUidHeldByAnotherName_IsRefusedWithTheHolderHref()
    {
        await writer.PutAsync(userId, calendarId, "a.ics", Event("shared", "Ada"), None);

        var outcome = await writer.PutAsync(userId, calendarId, "b.ics", Event("shared", "Grace"), None);

        // RFC 4791 § 5.3.2.1: the href names the holder — the whole of what the client can act on.
        Assert.Equal(DavWriteStatus.UidConflict, outcome.Status);
        Assert.Equal(DavPaths.Event(userId, CalendarStore.DefaultDavName, "a.ics"), outcome.ConflictHref);
        Assert.Single(context.CalendarEvents);
    }

    [Fact]
    public async Task ReplacingAResourceWithADifferentUid_IsAUidConflict()
    {
        // RFC 4791 § 5.3.2.1, no-uid-conflict, second half: « or overwrite an existing calendar
        // object resource with one that has a different UID property value ». The href a client
        // synced on would change identity under every other device it holds.
        await writer.PutAsync(userId, calendarId, "1.ics", Event("one@weesky.net", "avant"), None);

        var outcome = await writer.PutAsync(userId, calendarId, "1.ics", Event("two@weesky.net", "avant"), None);

        Assert.Equal(DavWriteStatus.UidConflict, outcome.Status);
    }

    [Fact]
    public async Task ReplacingAResourceWithTheSameUid_IsStillAReplacement()
    {
        await writer.PutAsync(userId, calendarId, "1.ics", Event("one@weesky.net", "avant"), None);

        var outcome = await writer.PutAsync(userId, calendarId, "1.ics", Event("one@weesky.net", "apres"), None);

        Assert.Equal(DavWriteStatus.Replaced, outcome.Status);
    }

    [Fact]
    public async Task TheSameUidInAnotherCalendar_IsNoConflict()
    {
        var other = await GivenAnotherCalendar("home");
        await writer.PutAsync(userId, other, "theirs.ics", Event("shared", "Ada"), None);

        var outcome = await writer.PutAsync(userId, calendarId, "mine.ics", Event("shared", "Grace"), None);

        // RFC 4791 § 4.1: a UID is unique per collection, not per user — under another name, or
        // the holder check would have excused it as the resource's own.
        Assert.Equal(DavWriteStatus.Created, outcome.Status);
        Assert.Equal(2, await context.CalendarEvents.CountAsync(e => e.Uid == "shared", None));
    }

    [Fact]
    public async Task AFullCalendar_IsRefusedAsCollectionFull()
    {
        await GivenTheCalendarIsFull();

        var outcome = await writer.PutAsync(userId, calendarId, "new.ics", Event("u1", "Ada"), None);

        Assert.Equal(DavWriteStatus.CollectionFull, outcome.Status);
    }

    [Fact]
    public async Task AFullCalendar_StillAcceptsAReplacement()
    {
        await writer.PutAsync(userId, calendarId, "a.ics", Event("u1", "Ada"), None);
        await GivenTheCalendarIsFull(CalendarEventStore.MaxPerCalendar - 1);

        var outcome = await writer.PutAsync(userId, calendarId, "a.ics", Event("u1", "Grace"), None);

        // The ceiling bounds the count of resources; a replacement adds none.
        Assert.Equal(DavWriteStatus.Replaced, outcome.Status);
    }

    [Fact]
    public async Task ACalendarOfAnotherUser_IsNotFound()
    {
        var outcome = await writer.PutAsync(Guid.NewGuid(), calendarId, "a.ics", Event("u1", "Ada"), None);

        // Judged inside the gate: the calendar is read under the lock, scoped by user.
        Assert.Equal(DavWriteStatus.NotFound, outcome.Status);
        Assert.Empty(context.CalendarEvents);
    }

    [Theory]
    [MemberData(nameof(EveryGuard))]
    public async Task EveryGuard_RefusesWithItsStatusAndItsPrecondition(
        string ics, DavWriteStatus status, IcsPrecondition precondition)
    {
        var outcome = await writer.PutAsync(userId, calendarId, "a.ics", ics, None);

        // The precondition rides on the outcome: the XML answer names it without a second read.
        Assert.Equal(status, outcome.Status);
        Assert.Equal(precondition, outcome.Precondition);
        Assert.Empty(context.CalendarEvents);
        Assert.Equal(0, sync.RankCalls);
    }

    public static TheoryData<string, DavWriteStatus, IcsPrecondition> EveryGuard() => new()
    {
        { "not a calendar at all", DavWriteStatus.InvalidCard, IcsPrecondition.ValidCalendarData },
        { Event("u1", "Ada").Replace("VERSION:2.0", "VERSION:1.0"), DavWriteStatus.UnsupportedVersion, IcsPrecondition.SupportedCalendarData },
        { Ics.Todo(), DavWriteStatus.UnsupportedComponent, IcsPrecondition.SupportedCalendarComponent },
        { Ics.Events(("a", null), ("b", null)), DavWriteStatus.InvalidCard, IcsPrecondition.ValidCalendarObjectResource },
        { Ics.Events(("a", null), ("a", null)), DavWriteStatus.InvalidCard, IcsPrecondition.ValidCalendarObjectResource },
        { EventWithoutStart(), DavWriteStatus.InvalidCard, IcsPrecondition.ValidCalendarData },
        { Ics.DensityBomb(), DavWriteStatus.TooManyInstances, IcsPrecondition.MaxInstances },
        { Ics.Padded(IcsGuards.MaxIcsBytes + 1), DavWriteStatus.TooLarge, IcsPrecondition.MaxResourceSize },
    };

    [Fact]
    public async Task AnAcceptedFile_CarriesNoPrecondition()
    {
        var outcome = await writer.PutAsync(userId, calendarId, "a.ics", Event("u1", "Ada"), None);

        Assert.Null(outcome.Precondition);
    }

    [Fact]
    public async Task TheRank_IsTakenBeforeAnyRowIsTouched()
    {
        await writer.PutAsync(userId, calendarId, "a.ics", Event("u1", "Ada"), None);
        using var fresh = new PreferencesTestDbContext(database);
        var calendarsReadAtRank = -1;
        var throwing = new Mock<ICalendarSyncStore>();
        throwing.Setup(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => calendarsReadAtRank = fresh.ChangeTracker.Entries<CalendarRow>().Count())
            .ThrowsAsync(new InvalidOperationException("no ambient transaction"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => NewWriter(fresh, throwing.Object)
            .PutAsync(userId, calendarId, "a.ics", Event("u1", "Grace"), None));

        // The state row's lock is the FIRST statement of the transaction: the collection row is
        // read after it, and when the rank cannot be taken no archive has happened.
        Assert.Equal(0, calendarsReadAtRank);
        Assert.Equal("Ada", (await RowOf("a.ics")).Summary);
        throwing.Verify(s => s.ArchiveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<RevisionCause>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PuttingOverATombstonedName_LiftsTheTombstone()
    {
        await writer.PutAsync(userId, calendarId, "a.ics", Event("u1", "Ada"), None);
        await writer.DeleteAsync(userId, calendarId, "a.ics", None);
        Assert.Single(context.CalendarTombstones);

        await writer.PutAsync(userId, calendarId, "a.ics", Event("u2", "Grace"), None);

        // A tombstone and a living resource must never coexist on one name.
        Assert.Empty(context.CalendarTombstones);
    }

    [Fact]
    public async Task Deleting_ArchivesBuriesAndRemovesTheAttendees()
    {
        await writer.PutAsync(userId, calendarId, "a.ics", Ics.WithAttendees(), None);
        Assert.NotEmpty(context.CalendarAttendees);

        var outcome = await writer.DeleteAsync(userId, calendarId, "a.ics", None);

        Assert.Equal(DavWriteStatus.Deleted, outcome.Status);
        Assert.Equal(2ul, outcome.Sequence);
        Assert.Equal(RevisionCause.Delete, Assert.Single(context.CalendarRevisions).Cause);
        // Buried under the very rank the deletion took, never another.
        var tombstone = Assert.Single(context.CalendarTombstones);
        Assert.Equal("a.ics", tombstone.DavName);
        Assert.Equal(2ul, tombstone.SyncSequence);
        Assert.Empty(context.CalendarEvents);
        Assert.Empty(context.CalendarAttendees);
    }

    [Fact]
    public async Task AConditionalDelete_WithAStaleTag_IsRefusedWithoutATombstone()
    {
        await writer.PutAsync(userId, calendarId, "a.ics", Event("u1", "Ada"), None);
        var ranks = sync.RankCalls;

        var outcome = await writer.DeleteAsync(userId, calendarId, "a.ics", None, ifMatch: "\"stale\"");

        Assert.Equal(DavWriteStatus.PreconditionFailed, outcome.Status);
        Assert.Empty(context.CalendarTombstones);
        Assert.Equal(ranks, sync.RankCalls);
        Assert.Single(context.CalendarEvents);
    }

    [Fact]
    public async Task AConditionalDelete_WithTheTagItRead_Deletes()
    {
        var created = await writer.PutAsync(userId, calendarId, "a.ics", Event("u1", "Ada"), None);

        Assert.Equal(DavWriteStatus.Deleted,
            (await writer.DeleteAsync(userId, calendarId, "a.ics", None, ifMatch: created.Etag)).Status);
    }

    [Fact]
    public async Task DeletingWhatIsNotThere_IsNotFound() =>
        Assert.Equal(DavWriteStatus.NotFound,
            (await writer.DeleteAsync(userId, calendarId, "never.ics", None)).Status);

    [Fact]
    public async Task DeletingAnEventOfAnotherCalendar_IsNotFound()
    {
        var other = await GivenAnotherCalendar("home");
        await writer.PutAsync(userId, other, "a.ics", Event("u1", "Ada"), None);

        Assert.Equal(DavWriteStatus.NotFound,
            (await writer.DeleteAsync(userId, calendarId, "a.ics", None)).Status);
        Assert.Single(context.CalendarEvents);
    }

    [Fact]
    public async Task DeletingTheWholeCalendar_BuriesEveryResourceInBatches()
    {
        // One over CalendarStore.DeleteBatch: two batch transactions, two ranks, every name buried.
        const int events = CalendarStore.DeleteBatch + 1;
        for (var i = 0; i < events; i++)
            await writer.PutAsync(userId, calendarId, $"e{i}.ics", Event($"u{i}", $"N{i}"), None);
        var ranks = sync.RankCalls;

        var outcome = await writer.DeleteAllAsync(userId, calendarId, None);

        Assert.Equal(DavWriteStatus.Deleted, outcome.Status);
        Assert.Equal(0ul, outcome.Sequence);
        Assert.Equal(ranks + 2, sync.RankCalls);
        Assert.Empty(context.CalendarEvents);
        Assert.Equal(events, context.CalendarTombstones.Count());
        Assert.Equal(events, context.CalendarRevisions.Count(r => r.Cause == RevisionCause.Delete));
        // Each batch's tombstones sit at that batch's own rank.
        Assert.Equal(2, context.CalendarTombstones.Select(t => t.SyncSequence).Distinct().Count());
    }

    [Fact]
    public async Task DeletingAnEmptyCalendar_IsDeletedAndWakesNobody()
    {
        var outcome = await writer.DeleteAllAsync(userId, calendarId, None);

        Assert.Equal(DavWriteStatus.Deleted, outcome.Status);
        Assert.Equal(0, sync.RankCalls);
        Assert.Empty(context.CalendarTombstones);
    }

    [Fact]
    public async Task DeletingTheWholeCalendar_TouchesOnlyItself()
    {
        var other = await GivenAnotherCalendar("home");
        await writer.PutAsync(userId, other, "theirs.ics", Event("u5", "Ada"), None);
        await writer.PutAsync(userId, calendarId, "mine.ics", Event("u1", "Ada"), None);

        await writer.DeleteAllAsync(userId, calendarId, None);

        Assert.Equal("theirs.ics", Assert.Single(context.CalendarEvents).DavName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyingACalendarThatIsNotTheUsers_IsNotFoundAndNot204(bool exists)
    {
        var target = exists ? await GivenAnotherCalendar("theirs", Guid.NewGuid()) : Guid.NewGuid();

        var outcome = await writer.DeleteAllAsync(userId, target, None);

        // Reading no resource is not the same as emptying a collection: Deleted here would answer
        // 204 over a calendar this account does not hold, or over none at all.
        Assert.Equal(DavWriteStatus.NotFound, outcome.Status);
        Assert.Equal(0, sync.RankCalls);
    }

    [Fact]
    public async Task ARejectedBody_IsArchivedWithoutARank()
    {
        var archived = await writer.ArchiveRejectedAsync(userId, calendarId, "a.ics", Event("u1", "Ada"), None);

        Assert.True(archived);
        Assert.Equal(0, sync.RankCalls);
        var revision = Assert.Single(context.CalendarRevisions);
        Assert.Equal(RevisionCause.Rejected, revision.Cause);
        Assert.Equal("u1", revision.Uid);
        Assert.Equal("a.ics", revision.DavName);
    }

    [Fact]
    public async Task ARejectedBodyThatDoesNotParse_IsStillArchived_WithNoUid()
    {
        Assert.True(await writer.ArchiveRejectedAsync(userId, calendarId, "a.ics", "garbage but valid utf-8", None));

        Assert.Null(Assert.Single(context.CalendarRevisions).Uid);
    }

    [Fact]
    public async Task ARejectedBodyOverTheCeiling_IsNotArchived()
    {
        Assert.False(await writer.ArchiveRejectedAsync(
            userId, calendarId, "a.ics", Ics.Padded(IcsGuards.MaxIcsBytes + 1), None));

        Assert.Empty(context.CalendarRevisions);
    }

    [Theory]
    [InlineData(MySqlErrors.LockWaitTimeout)]
    [InlineData(MySqlErrors.Deadlock)]
    public async Task APutLosingALockRace_IsBusyRatherThanReplayed(int number)
    {
        using var racing = new RacingDbContext(database, () => { },
            () => new DbUpdateException("save failed", MySqlErrors.With(number)));
        var racingSync = new TestCalendarSyncStore(racing);

        var outcome = await NewWriter(racing, racingSync)
            .PutAsync(userId, calendarId, "a.ics", Event("u1", "Ada"), None);

        // The 1205 arrives inside a DbUpdateException, the same shape the unique-index race takes:
        // replaying it would only wait again, so it is told apart and answered as busy — once.
        Assert.Equal(DavWriteStatus.Busy, outcome.Status);
        Assert.Equal(1, racingSync.RankCalls);
    }

    [Theory]
    [InlineData(MySqlErrors.LockWaitTimeout)]
    [InlineData(MySqlErrors.Deadlock)]
    public async Task ADeleteLosingALockRace_IsBusyRatherThanAFault(int number)
    {
        await writer.PutAsync(userId, calendarId, "a.ics", Event("u1", "Ada"), None);
        using var racing = new RacingDbContext(database, () => { },
            () => new DbUpdateException("save failed", MySqlErrors.With(number)), EntityState.Deleted);

        var outcome = await NewWriter(racing, new TestCalendarSyncStore(racing))
            .DeleteAsync(userId, calendarId, "a.ics", None);

        Assert.Equal(DavWriteStatus.Busy, outcome.Status);
    }

    [Theory]
    [InlineData(MySqlErrors.LockWaitTimeout)]
    [InlineData(MySqlErrors.Deadlock)]
    public async Task ArchivingARejectedBody_LosingALockRace_AnswersFalseRatherThanEscaping(int number)
    {
        var throwing = new Mock<ICalendarSyncStore>();
        throwing.Setup(s => s.ArchiveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<RevisionCause>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("save failed", MySqlErrors.With(number)));

        Assert.False(await NewWriter(context, throwing.Object)
            .ArchiveRejectedAsync(userId, calendarId, "a.ics", Event("u1", "Ada"), None));
    }

    [Fact]
    public async Task TwoCreatingPuts_TheLoserReplaysAsAReplacement()
    {
        // The race no InMemory index can stage on its own: the loser passes the existence
        // pre-check, the winner's row lands, and the loser's insert dies on (calendar_id, dav_name).
        var winner = Event("u1", "Winner");
        using var racing = new RacingDbContext(database, () => Seed("a.ics", winner));

        var outcome = await NewWriter(racing, new TestCalendarSyncStore(racing))
            .PutAsync(userId, calendarId, "a.ics", Event("u1", "Loser"), None);

        // What the same PUT arrived a second later would have been: a replacement of the winner.
        Assert.Equal(DavWriteStatus.Replaced, outcome.Status);
        using var check = new PreferencesTestDbContext(database);
        Assert.Equal("Loser", check.CalendarEvents.Single(e => e.DavName == "a.ics").Summary);
        Assert.Equal(winner, Assert.Single(check.CalendarRevisions.Where(r => r.Cause == RevisionCause.Put)).IcsRaw);
    }

    [Fact]
    public async Task ACreateOnlyPut_LosingTheRace_IsRefusedWithoutWriting()
    {
        var winner = Event("u1", "Winner");
        using var racing = new RacingDbContext(database, () => Seed("a.ics", winner));

        var outcome = await NewWriter(racing, new TestCalendarSyncStore(racing))
            .PutAsync(userId, calendarId, "a.ics", Event("u1", "Loser"), None, createOnly: true);

        // The If-None-Match: * intent reaches the replay: it refuses INSTEAD of replacing.
        Assert.Equal(DavWriteStatus.AlreadyExists, outcome.Status);
        using var check = new PreferencesTestDbContext(database);
        Assert.Equal(winner, check.CalendarEvents.Single(e => e.DavName == "a.ics").IcsRaw);
        Assert.Empty(check.CalendarRevisions);
    }

    [Fact]
    public async Task ARacedUid_IsTranslatedToAConflictWithTheWinnersHref()
    {
        // Same race, other index: the winner lands the UID under ANOTHER name, so the replay would
        // only die again — the translation is a refusal carrying the winner's href.
        using var racing = new RacingDbContext(database, () => Seed("b.ics", Event("u1", "Winner")));

        var outcome = await NewWriter(racing, new TestCalendarSyncStore(racing))
            .PutAsync(userId, calendarId, "a.ics", Event("u1", "Loser"), None);

        Assert.Equal(DavWriteStatus.UidConflict, outcome.Status);
        Assert.Equal(DavPaths.Event(userId, CalendarStore.DefaultDavName, "b.ics"), outcome.ConflictHref);
    }

    private static DavCalendarWriter NewWriter(PreferencesDbContext context, ICalendarSyncStore sync) =>
        new(new CalendarEventStore(context, sync, NullLogger<CalendarEventStore>.Instance), sync,
            context, NullLogger<DavCalendarWriter>.Instance);

    private static string Event(string uid, string summary) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//weesky//tests//EN\r\n"
        + $"BEGIN:VEVENT\r\nUID:{uid}\r\nDTSTAMP:20260901T080000Z\r\n"
        + "DTSTART:20260907T090000Z\r\nDTEND:20260907T100000Z\r\n"
        + $"SUMMARY:{summary}\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    private static string EventWithoutStart() =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//weesky//tests//EN\r\n"
        + "BEGIN:VEVENT\r\nUID:nostart\r\nDTSTAMP:20260901T080000Z\r\nSUMMARY:No start\r\nEND:VEVENT\r\n"
        + "END:VCALENDAR\r\n";

    private Task<CalendarEvent> RowOf(string davName) =>
        context.CalendarEvents.SingleAsync(e => e.CalendarId == calendarId && e.DavName == davName, None);

    private async Task<Guid> GivenAnotherCalendar(string davName, Guid? owner = null)
    {
        var row = new CalendarRow
        {
            Id = Guid.NewGuid(), UserId = owner ?? userId, DavName = davName, DisplayName = davName,
            Description = string.Empty, Color = "#336699", Order = 2,
            TimeZone = CalendarStoreTestFactory.Zone, IsVisible = true,
        };
        context.Calendars.Add(row);
        await context.SaveChangesAsync(None);
        return row.Id;
    }

    private async Task GivenTheCalendarIsFull(int count = CalendarEventStore.MaxPerCalendar)
    {
        for (var i = 0; i < count; i++)
        {
            context.CalendarEvents.Add(new CalendarEvent
            {
                Id = Guid.NewGuid(), CalendarId = calendarId, UserId = userId,
                Uid = Guid.NewGuid().ToString(), DavName = $"filler{i}.ics", IcsRaw = "x",
                IcsHash = "h", SyncSequence = 1, UpdatedAt = DateTime.UtcNow,
            });
        }

        await context.SaveChangesAsync(None);
    }

    /// <summary>The winner's row, committed through its own context.</summary>
    private void Seed(string davName, string ics)
    {
        using var competitor = new PreferencesTestDbContext(database);
        competitor.CalendarEvents.Add(new CalendarEvent
        {
            Id = Guid.NewGuid(), CalendarId = calendarId, UserId = userId, Uid = "u1",
            DavName = davName, IcsRaw = ics, IcsHash = IcsDocument.HashOf(ics), SyncSequence = 1,
            UpdatedAt = DateTime.UtcNow,
        });
        competitor.SaveChanges();
    }

    /// <summary>
    /// A context whose first save touching an event in <paramref name="on"/> runs
    /// <paramref name="competitor"/> and then throws <paramref name="thrown"/> — the only way the
    /// InMemory provider, which enforces no index and arbitrates no lock, can stage a race.
    /// </summary>
    private sealed class RacingDbContext(
        string databaseName, Action competitor, Func<Exception> thrown,
        EntityState on = EntityState.Added)
        : PreferencesDbContext(OptionsOf(databaseName))
    {
        private bool raced;

        internal RacingDbContext(string databaseName, Action competitor)
            : this(databaseName, competitor,
                () => new DbUpdateException("Duplicate entry, as the unique index would say"))
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!raced && ChangeTracker.Entries<CalendarEvent>().Any(e => e.State == on))
            {
                raced = true;
                competitor();
                throw thrown();
            }

            return base.SaveChangesAsync(cancellationToken);
        }

        private static DbContextOptions<PreferencesDbContext> OptionsOf(string name) =>
            new DbContextOptionsBuilder<PreferencesDbContext>()
                .UseInMemoryDatabase(name, PreferencesTestDbContext.Root)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
    }
}
