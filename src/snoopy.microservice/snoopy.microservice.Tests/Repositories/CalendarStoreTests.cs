using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class CalendarStoreTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task EnsureDefault_CreatesOnce_WithBrowserZone_AndItsState()
    {
        var db = nameof(EnsureDefault_CreatesOnce_WithBrowserZone_AndItsState);
        var user = Guid.NewGuid();

        var first = await Store(db).EnsureDefaultAsync(user, "Europe/Brussels", None);
        var again = await Store(db).EnsureDefaultAsync(user, "America/New_York", None);

        // The browser's zone decides once, when the account is first opened: a second call from a
        // laptop abroad must not silently move every floating event the collection holds.
        Assert.Equal(first.Id, again.Id);
        Assert.Equal("Europe/Brussels", again.TimeZone);
        Assert.True(again.IsDefault);
        Assert.NotNull(await new PreferencesTestDbContext(db).CalendarSyncStates.FindAsync(first.Id));
    }

    [Fact]
    public async Task Create_TakesNextPaletteColour_LastOrder_IdAsDavName()
    {
        var db = nameof(Create_TakesNextPaletteColour_LastOrder_IdAsDavName);
        var user = Guid.NewGuid();
        await Store(db).EnsureDefaultAsync(user, "Europe/Brussels", None);

        var id = (await Store(db).CreateAsync(
            user, new CalendarWrite("Work", null, null, null), "Europe/Brussels", None)).Value;

        var view = (await Store(db).ListAsync(user, None)).Single(c => c.Id == id);
        Assert.Equal(id.ToString(), view.DavName);
        Assert.Equal(CalendarPalette.Colours[1], view.Color);
        Assert.Equal(1, view.Order);
        Assert.False(view.IsDefault);
        Assert.NotNull(await new PreferencesTestDbContext(db).CalendarSyncStates.FindAsync(id));
    }

    [Fact]
    public async Task Create_RefusesTheTwentyFirst()
    {
        var db = nameof(Create_RefusesTheTwentyFirst);
        var user = Guid.NewGuid();
        await Store(db).EnsureDefaultAsync(user, "Europe/Brussels", None);

        for (var i = 1; i < CalendarStore.MaxPerUser; i++)
        {
            Assert.True((await Store(db).CreateAsync(
                user, new CalendarWrite($"c{i}", null, null, null), "Europe/Brussels", None)).IsSuccess);
        }

        var refused = await Store(db).CreateAsync(
            user, new CalendarWrite("one too many", null, null, null), "Europe/Brussels", None);

        Assert.Equal(CalendarStore.CapReached, refused.Error);
    }

    // The count and the write are one transaction: a refusal must never leave a calendar row
    // without the sync state that makes it visible, nor the reverse.
    [Fact]
    public async Task Create_AtTheCap_LeavesNoPartialState()
    {
        var db = nameof(Create_AtTheCap_LeavesNoPartialState);
        var user = Guid.NewGuid();
        await Store(db).EnsureDefaultAsync(user, "Europe/Brussels", None);
        for (var i = 1; i < CalendarStore.MaxPerUser; i++)
        {
            Assert.True((await Store(db).CreateAsync(
                user, new CalendarWrite($"c{i}", null, null, null), "Europe/Brussels", None)).IsSuccess);
        }

        var refused = await Store(db).CreateAsync(
            user, new CalendarWrite("one too many", null, null, null), "Europe/Brussels", None);

        Assert.Equal(CalendarStore.CapReached, refused.Error);
        var context = new PreferencesTestDbContext(db);
        Assert.Equal(CalendarStore.MaxPerUser, await context.Calendars.CountAsync(c => c.UserId == user, None));
        Assert.Equal(CalendarStore.MaxPerUser, await context.CalendarSyncStates.CountAsync(None));
    }

    [Fact]
    public async Task Delete_RefusesDefault_ArchivesEventsInBatches_RemovesStateAndTombstones()
    {
        var db = nameof(Delete_RefusesDefault_ArchivesEventsInBatches_RemovesStateAndTombstones);
        var user = Guid.NewGuid();
        var fallback = await Store(db).EnsureDefaultAsync(user, "Europe/Brussels", None);
        var work = (await Store(db).CreateAsync(
            user, new CalendarWrite("Work", null, null, null), "Europe/Brussels", None)).Value;

        var seed = new PreferencesTestDbContext(db);
        seed.CalendarEvents.AddRange(Enumerable.Range(0, 250).Select(i => Stored(user, work, i)));
        seed.CalendarTombstones.Add(new CalendarTombstone
        {
            CalendarId = work, DavName = "gone.ics", SyncSequence = 1, DeletedAt = DateTime.UtcNow
        });
        await seed.SaveChangesAsync(None);

        var (store, sync) = CalendarStoreTestFactory.CalendarsWithSync(db);
        Assert.True((await store.DeleteAsync(user, work, None)).IsSuccess);

        var after = new PreferencesTestDbContext(db);
        // Every event archived, in three transactions — two hundred and fifty at a hundred a batch
        // — plus the tail that removes the state and the tombstones: four ranks, each taken FIRST
        // in its own transaction, which is what keeps this door's lock order the module's own.
        Assert.Equal(250, await after.CalendarRevisions.CountAsync(
            r => r.CalendarId == work && r.Cause == RevisionCause.Delete, None));
        Assert.Equal(4, sync.RankCalls);
        Assert.All(after.CalendarRevisions.Where(r => r.CalendarId == work), r => Assert.Null(r.EventId));
        Assert.Empty(after.CalendarEvents.Where(e => e.CalendarId == work));
        Assert.Empty(after.CalendarTombstones.Where(t => t.CalendarId == work));
        Assert.Null(await after.CalendarSyncStates.FindAsync(work));
        Assert.Empty(after.Calendars.Where(c => c.Id == work));

        // The one collection no deletion may take: a user with none has nowhere to write.
        var refused = await Store(db).DeleteAsync(user, fallback.Id, None);
        Assert.Equal(CalendarStore.NotDeletable, refused.Error);
    }

    [Fact]
    public async Task SetVisible_DoesNotTouchTheSequence()
    {
        var db = nameof(SetVisible_DoesNotTouchTheSequence);
        var (_, user, calendar) = await CalendarStoreTestFactory.SeedAsync(db);
        await CalendarStoreTestFactory.Events(db).CreateAsync(
            user, CalendarStoreTestFactory.Write(calendar), None);

        var before = await new PreferencesTestDbContext(db).CalendarSyncStates
            .SingleAsync(s => s.CalendarId == calendar, None);
        var seq = before.Seq;

        Assert.True((await Store(db).SetVisibleAsync(user, calendar, false, None)).IsSuccess);

        // The checkbox is a display state, never projected to DAV (décision 2): advancing the
        // counter here would make every phone resync a collection nothing in it changed.
        var after = await new PreferencesTestDbContext(db).CalendarSyncStates
            .SingleAsync(s => s.CalendarId == calendar, None);
        Assert.Equal(seq, after.Seq);
        Assert.False((await Store(db).ListAsync(user, None)).Single(c => c.Id == calendar).IsVisible);
    }

    [Fact]
    public async Task Update_ChangesTheLabel_AndLeavesTheNameAlone()
    {
        var db = nameof(Update_ChangesTheLabel_AndLeavesTheNameAlone);
        var user = Guid.NewGuid();
        var created = await Store(db).EnsureDefaultAsync(user, "Europe/Brussels", None);

        Assert.True((await Store(db).UpdateAsync(
            user, created.Id, new CalendarWrite("Famille", "Les anniversaires", "#123456", 3), None))
            .IsSuccess);

        var view = (await Store(db).ListAsync(user, None)).Single();
        Assert.Equal("Famille", view.DisplayName);
        Assert.Equal("#123456", view.Color);
        Assert.Equal(3, view.Order);
        // The dav_name is what a client syncs on, so a rename never moves it.
        Assert.Equal(CalendarStore.DefaultDavName, view.DavName);
    }

    /// <summary>
    /// The colour goes out verbatim on the export's <c>COLOR</c> line, so a value carrying a break
    /// would forge iCalendar lines in a file the user then hands to another client. Refused where
    /// it enters, and folded to one spelling so two calendars cannot hold the same colour twice.
    /// </summary>
    [Fact]
    public async Task AColourIsSixHexDigits_OrItIsRefused()
    {
        var db = nameof(AColourIsSixHexDigits_OrItIsRefused);
        var user = Guid.NewGuid();
        var created = await Store(db).EnsureDefaultAsync(user, "Europe/Brussels", None);

        Assert.Equal(CalendarStore.BadColour,
            (await Store(db).UpdateAsync(
                user, created.Id, new CalendarWrite("Famille", null, "#fff\r\nCOLOR:red", 0), None)).Error);
        Assert.Equal(CalendarStore.BadColour,
            (await Store(db).CreateAsync(
                user, new CalendarWrite("Work", null, "rebeccapurple", null), "Europe/Brussels", None)).Error);

        // Apple's alpha channel is dropped, and the digits folded.
        Assert.True((await Store(db).UpdateAsync(
            user, created.Id, new CalendarWrite("Famille", null, "#AABBCC80", 0), None)).IsSuccess);
        Assert.Equal("#aabbcc", (await Store(db).ListAsync(user, None)).Single().Color);
    }

    /// <summary>The label, the colour and the order are display state, never projected to DAV: the
    /// counter must not move, or every phone resyncs a collection nothing in it changed.</summary>
    [Fact]
    public async Task Update_DoesNotTouchTheSequence()
    {
        var db = nameof(Update_DoesNotTouchTheSequence);
        var (_, user, calendar) = await CalendarStoreTestFactory.SeedAsync(db);
        await CalendarStoreTestFactory.Events(db).CreateAsync(
            user, CalendarStoreTestFactory.Write(calendar), None);
        var seq = (await new PreferencesTestDbContext(db).CalendarSyncStates
            .SingleAsync(s => s.CalendarId == calendar, None)).Seq;

        Assert.True((await Store(db).UpdateAsync(
            user, calendar, new CalendarWrite("Famille", "Les anniversaires", "#123456", 3), None)).IsSuccess);

        Assert.Equal(seq, (await new PreferencesTestDbContext(db).CalendarSyncStates
            .SingleAsync(s => s.CalendarId == calendar, None)).Seq);
    }

    /// <summary>The ids are read before any transaction opens, so a collection holding nothing
    /// spends no batch rank at all — only the tail that removes the state and the tombstones.</summary>
    [Fact]
    public async Task Delete_AnEmptyCalendar_SpendsTheTailRankAlone()
    {
        var db = nameof(Delete_AnEmptyCalendar_SpendsTheTailRankAlone);
        var user = Guid.NewGuid();
        await Store(db).EnsureDefaultAsync(user, "Europe/Brussels", None);
        var work = (await Store(db).CreateAsync(
            user, new CalendarWrite("Work", null, null, null), "Europe/Brussels", None)).Value;

        var (store, sync) = CalendarStoreTestFactory.CalendarsWithSync(db);
        Assert.True((await store.DeleteAsync(user, work, None)).IsSuccess);

        Assert.Equal(1, sync.RankCalls);
        Assert.Empty(new PreferencesTestDbContext(db).Calendars.Where(c => c.Id == work));
    }

    /// <summary>
    /// Two first requests on the same fresh account: the unique index on (user_id, dav_name) names
    /// the winner, and "ensure" owes its caller that row rather than the loser's exception. The
    /// InMemory provider enforces no unique index, so the race is staged by the context itself.
    /// </summary>
    [Fact]
    public async Task EnsureDefault_WhenAnotherRequestLandedFirst_AnswersTheWinner()
    {
        var db = nameof(EnsureDefault_WhenAnotherRequestLandedFirst_AnswersTheWinner);
        var user = Guid.NewGuid();
        CalendarView? winner = null;

        var context = new RacingPreferencesDbContext(db, async () =>
            winner = await Store(db).EnsureDefaultAsync(user, "America/New_York", None));
        var loser = await new CalendarStore(context, new TestCalendarSyncStore(context))
            .EnsureDefaultAsync(user, "Europe/Brussels", None);

        Assert.NotNull(winner);
        Assert.Equal(winner.Id, loser.Id);
        // The winner's zone, not the loser's: the row that landed is the one every event is read in.
        Assert.Equal("America/New_York", loser.TimeZone);
        Assert.Single(new PreferencesTestDbContext(db).Calendars.Where(c => c.UserId == user));
    }

    [Fact]
    public async Task EveryDoor_RefusesAnotherUsersCalendar()
    {
        var db = nameof(EveryDoor_RefusesAnotherUsersCalendar);
        var mine = await Store(db).EnsureDefaultAsync(Guid.NewGuid(), "Europe/Brussels", None);
        var stranger = Guid.NewGuid();

        // Not "forbidden" but "not found": telling them apart would confirm the id exists.
        Assert.Equal(CalendarStore.NotFound,
            (await Store(db).UpdateAsync(stranger, mine.Id, new CalendarWrite("x", null, null, null), None)).Error);
        Assert.Equal(CalendarStore.NotFound,
            (await Store(db).SetVisibleAsync(stranger, mine.Id, false, None)).Error);
        Assert.Equal(CalendarStore.NotFound,
            (await Store(db).DeleteAsync(stranger, mine.Id, None)).Error);
    }

    private static CalendarStore Store(string db) => CalendarStoreTestFactory.Calendars(db);

    /// <summary>A stored resource seeded straight into the table: the deletion archives whatever
    /// <c>ics_raw</c> holds, and composing 250 of them through the editor would prove nothing.</summary>
    private static CalendarEvent Stored(Guid user, Guid calendar, int index)
    {
        var id = Guid.NewGuid();
        return new CalendarEvent
        {
            Id = id,
            CalendarId = calendar,
            UserId = user,
            Uid = id.ToString(),
            DavName = $"{id}.ics",
            Summary = $"Seeded {index}",
            IcsRaw = Ics.Single(start: "DTSTART:20260907T090000Z", end: "DTEND:20260907T100000Z"),
            IcsHash = index.ToString(),
            SyncSequence = 1,
            StartsAt = new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
            EndsAt = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc),
            FirstOccurrence = new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
            LastOccurrence = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc),
            UpdatedAt = DateTime.UtcNow
        };
    }
}
