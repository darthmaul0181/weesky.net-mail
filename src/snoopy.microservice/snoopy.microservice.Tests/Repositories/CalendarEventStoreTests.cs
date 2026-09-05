using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class CalendarEventStoreTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task Create_ProjectsColumns_HashesAndRanks_UidIsId()
    {
        var (db, user, cal) = await Seed(nameof(Create_ProjectsColumns_HashesAndRanks_UidIsId));

        var id = (await Events(db).CreateAsync(user, Write(cal, start: Local(2026, 9, 7, 9)), None)).Value;

        var row = await new PreferencesTestDbContext(db).CalendarEvents.SingleAsync(None);
        Assert.Equal(id.ToString(), row.Uid);
        Assert.Equal($"{id}.ics", row.DavName);
        // Nine in Brussels in September is seven in UTC: the column is an instant, always.
        Assert.Equal(new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc), row.StartsAt);
        Assert.Equal("Standup", row.Summary);
        Assert.Equal("Europe/Brussels", row.TimeZone);
        Assert.False(row.IsRecurring);
        Assert.Equal(IcsDocument.HashOf(row.IcsRaw), row.IcsHash);
        Assert.Equal(1UL, row.SyncSequence);
    }

    [Fact]
    public async Task Update_All_ArchivesWebmailRevision_AdvancesRank_SkipsWhenNothingChanged()
    {
        var (db, user, cal) = await Seed(
            nameof(Update_All_ArchivesWebmailRevision_AdvancesRank_SkipsWhenNothingChanged));
        var id = (await Events(db).CreateAsync(user, Write(cal), None)).Value;
        var before = (await Events(db).GetAsync(user, id, None))!;

        Assert.True((await Events(db).UpdateAsync(
            user, id, EditScope.All, null, before.Fields, before.IcsHash, None)).IsSuccess);

        // Nothing moved: no rank, no revision, no client woken for a version that is not one.
        Assert.Equal(before.IcsHash, (await Events(db).GetAsync(user, id, None))!.IcsHash);
        Assert.Empty(new PreferencesTestDbContext(db).CalendarRevisions);

        Assert.True((await Events(db).UpdateAsync(
            user, id, EditScope.All, null, before.Fields with { Summary = "Renamed" },
            before.IcsHash, None)).IsSuccess);

        Assert.Equal(RevisionCause.Webmail,
            (await new PreferencesTestDbContext(db).CalendarRevisions.SingleAsync(None)).Cause);
        var row = await new PreferencesTestDbContext(db).CalendarEvents.SingleAsync(None);
        Assert.Equal(2UL, row.SyncSequence);
        Assert.Equal("Renamed", row.Summary);
    }

    [Fact]
    public async Task Update_WithStaleHash_IsRefusedAsMoved()
    {
        var (db, user, cal) = await Seed(nameof(Update_WithStaleHash_IsRefusedAsMoved));
        var id = (await Events(db).CreateAsync(user, Write(cal), None)).Value;
        var before = (await Events(db).GetAsync(user, id, None))!;

        var refused = await Events(db).UpdateAsync(
            user, id, EditScope.All, null, before.Fields with { Summary = "Renamed" },
            "0000000000000000000000000000000000000000000000000000000000000000", None);

        // Refused before anything else: the refusal opens no transaction and takes no rank.
        Assert.Equal(CalendarEventStore.EventMoved, refused.Error);
        Assert.Empty(new PreferencesTestDbContext(db).CalendarRevisions);
        Assert.Equal(1UL, (await new PreferencesTestDbContext(db).CalendarEvents.SingleAsync(None)).SyncSequence);
    }

    /// <summary>
    /// The lost update the hash exists to prevent, and the one a check made before the lock cannot
    /// catch: this caller read the resource, somebody else saved it, and this caller's own context
    /// still believes the hash it holds is current. Only the re-read under the state lock sees it.
    /// </summary>
    [Fact]
    public async Task Update_WhenAnotherWriterLandedFirst_IsRefusedAsMoved()
    {
        var (db, user, cal) = await Seed(nameof(Update_WhenAnotherWriterLandedFirst_IsRefusedAsMoved));
        var id = (await Events(db).CreateAsync(user, Write(cal), None)).Value;

        // One store keeps the row tracked from a first save of its own — a live editor's session.
        var mine = Events(db);
        var seen = (await Events(db).GetAsync(user, id, None))!;
        Assert.True((await mine.UpdateAsync(
            user, id, EditScope.All, null, seen.Fields with { Summary = "First" }, seen.IcsHash, None)).IsSuccess);
        var read = (await Events(db).GetAsync(user, id, None))!;

        // Somebody else saves, on the very version this caller is still holding.
        Assert.True((await Events(db).UpdateAsync(
            user, id, EditScope.All, null, read.Fields with { Summary = "Theirs" }, read.IcsHash, None)).IsSuccess);

        var refused = await mine.UpdateAsync(
            user, id, EditScope.All, null, read.Fields with { Summary = "Mine" }, read.IcsHash, None);

        Assert.Equal(CalendarEventStore.EventMoved, refused.Error);
        var after = new PreferencesTestDbContext(db);
        Assert.Equal("Theirs", (await after.CalendarEvents.SingleAsync(None)).Summary);
        // Two writes landed, so two revisions: the refusal archived nothing.
        Assert.Equal(2, await after.CalendarRevisions.CountAsync(None));
    }

    /// <summary>
    /// A narrow scope on an event that does not repeat has no instance to carve out: left alone,
    /// the update duplicates the row under a second UID and the delete removes nothing while
    /// answering success.
    /// </summary>
    [Fact]
    public async Task ANarrowScopeOnAnEventThatDoesNotRepeat_IsTheWholeEvent()
    {
        var (db, user, cal) = await Seed(nameof(ANarrowScopeOnAnEventThatDoesNotRepeat_IsTheWholeEvent));
        var id = (await Events(db).CreateAsync(user, Write(cal), None)).Value;
        var read = (await Events(db).GetAsync(user, id, None))!;

        Assert.True((await Events(db).UpdateAsync(
            user, id, EditScope.ThisAndFollowing, "20260907T090000",
            read.Fields with { Summary = "Renamed" }, read.IcsHash, None)).IsSuccess);

        var row = await new PreferencesTestDbContext(db).CalendarEvents.SingleAsync(None);
        Assert.Equal("Renamed", row.Summary);
        Assert.Equal(id.ToString(), row.Uid);

        Assert.True((await Events(db).DeleteAsync(
            user, id, EditScope.This, "20260907T090000", None)).IsSuccess);

        Assert.Empty(new PreferencesTestDbContext(db).CalendarEvents);
        Assert.Single(new PreferencesTestDbContext(db).CalendarTombstones);
    }

    /// <summary>A cut at the series' own start leaves nothing before it: the whole series is what
    /// the user is editing, and a split would leave an empty original behind.</summary>
    [Fact]
    public async Task ThisAndFollowing_CutAtTheSeriesOwnStart_IsTheWholeSeries()
    {
        var (db, user, cal) = await Seed(nameof(ThisAndFollowing_CutAtTheSeriesOwnStart_IsTheWholeSeries));
        var id = (await Events(db).CreateAsync(
            user, Write(cal, repeat: CalendarStoreTestFactory.Weekly()), None)).Value;
        var read = (await Events(db).GetAsync(user, id, None))!;

        Assert.True((await Events(db).UpdateAsync(
            user, id, EditScope.ThisAndFollowing, "20260907T090000",
            read.Fields with { Summary = "Renamed" }, read.IcsHash, None)).IsSuccess);

        var row = await new PreferencesTestDbContext(db).CalendarEvents.SingleAsync(None);
        Assert.Equal("Renamed", row.Summary);
        Assert.True(row.IsRecurring);

        Assert.True((await Events(db).DeleteAsync(
            user, id, EditScope.ThisAndFollowing, "20260907T090000", None)).IsSuccess);

        Assert.Empty(new PreferencesTestDbContext(db).CalendarEvents);
    }

    [Fact]
    public async Task Update_RefusesWhenTheTargetIsFull_OrTheCutWouldOverfillIt()
    {
        var (db, user, cal) = await Seed(nameof(Update_RefusesWhenTheTargetIsFull_OrTheCutWouldOverfillIt));
        var full = (await CalendarStoreTestFactory.Calendars(db).CreateAsync(
            user, new CalendarWrite("Full", null, null, null), "Europe/Brussels", None)).Value;
        var id = (await Events(db).CreateAsync(
            user, Write(cal, repeat: CalendarStoreTestFactory.Weekly()), None)).Value;
        var read = (await Events(db).GetAsync(user, id, None))!;

        var seed = new PreferencesTestDbContext(db);
        seed.CalendarEvents.AddRange(Enumerable.Range(0, CalendarEventStore.MaxPerCalendar)
            .Select(_ => Filler(user, full)));
        // The source collection is one short of full, so the second half of a cut is the row over it.
        seed.CalendarEvents.AddRange(Enumerable.Range(0, CalendarEventStore.MaxPerCalendar - 1)
            .Select(_ => Filler(user, cal)));
        await seed.SaveChangesAsync(None);

        var moved = await Events(db).UpdateAsync(
            user, id, EditScope.All, null, read.Fields with { CalendarId = full }, read.IcsHash, None);
        Assert.Equal(CalendarEventStore.CapReached, moved.Error);

        var cut = await Events(db).UpdateAsync(
            user, id, EditScope.ThisAndFollowing, "20260921T090000",
            read.Fields with { Start = Local(2026, 9, 21, 10), End = Local(2026, 9, 21, 11) },
            read.IcsHash, None);
        Assert.Equal(CalendarEventStore.CapReached, cut.Error);

        // Refused after the rank was taken, so nothing of either attempt survives.
        Assert.Empty(new PreferencesTestDbContext(db).CalendarRevisions);
    }

    [Fact]
    public async Task Update_ThisOnly_ThenDelete_ThisOnly()
    {
        var (db, user, cal) = await Seed(nameof(Update_ThisOnly_ThenDelete_ThisOnly));
        var id = (await Events(db).CreateAsync(
            user, Write(cal, repeat: CalendarStoreTestFactory.Weekly()), None)).Value;
        var before = (await Events(db).GetAsync(user, id, None))!;

        Assert.True((await Events(db).UpdateAsync(
            user, id, EditScope.This, "20260914T090000",
            before.Fields with { Start = Local(2026, 9, 14, 11), End = Local(2026, 9, 14, 12) },
            before.IcsHash, None)).IsSuccess);

        var moved = await new PreferencesTestDbContext(db).CalendarEvents.SingleAsync(None);
        Assert.Equal(2, Occurrences(moved.IcsRaw, "BEGIN:VEVENT"));
        Assert.Contains("RECURRENCE-ID", moved.IcsRaw, StringComparison.Ordinal);

        Assert.True((await Events(db).DeleteAsync(
            user, id, EditScope.This, "20260914T090000", None)).IsSuccess);

        var pruned = await new PreferencesTestDbContext(db).CalendarEvents.SingleAsync(None);
        Assert.Equal(1, Occurrences(pruned.IcsRaw, "BEGIN:VEVENT"));
        Assert.Contains("EXDATE", pruned.IcsRaw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_ThisAndFollowing_CreatesASecondRow_InOneTransaction()
    {
        var (db, user, cal) = await Seed(
            nameof(Update_ThisAndFollowing_CreatesASecondRow_InOneTransaction));
        var id = (await Events(db).CreateAsync(
            user, Write(cal, repeat: CalendarStoreTestFactory.Weekly()), None)).Value;
        var before = (await Events(db).GetAsync(user, id, None))!;

        Assert.True((await Events(db).UpdateAsync(
            user, id, EditScope.ThisAndFollowing, "20260921T090000",
            before.Fields with { Start = Local(2026, 9, 21, 10), End = Local(2026, 9, 21, 11) },
            before.IcsHash, None)).IsSuccess);

        var rows = await new PreferencesTestDbContext(db).CalendarEvents.ToListAsync(None);
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(r => r.Uid).Distinct().Count());
        Assert.All(rows, r => Assert.Equal(cal, r.CalendarId));
        // One gesture, one version of the collection: both halves carry the same rank.
        Assert.All(rows, r => Assert.Equal(2UL, r.SyncSequence));
    }

    [Fact]
    public async Task Update_ToAnotherCalendar_TombstonesOld_RanksNew()
    {
        var (db, user, cal) = await Seed(nameof(Update_ToAnotherCalendar_TombstonesOld_RanksNew));
        var work = (await CalendarStoreTestFactory.Calendars(db).CreateAsync(
            user, new CalendarWrite("Work", null, null, null), "Europe/Brussels", None)).Value;
        var id = (await Events(db).CreateAsync(user, Write(cal), None)).Value;
        var before = (await Events(db).GetAsync(user, id, None))!;

        Assert.True((await Events(db).UpdateAsync(
            user, id, EditScope.All, null, before.Fields with { CalendarId = work },
            before.IcsHash, None)).IsSuccess);

        var context = new PreferencesTestDbContext(db);
        var row = await context.CalendarEvents.SingleAsync(None);
        Assert.Equal(work, row.CalendarId);
        // The name and the identity ride along untouched: only a collision would rename either.
        Assert.Equal($"{id}.ics", row.DavName);
        Assert.Equal(id.ToString(), row.Uid);
        // The new collection's own rank, its first; the tombstone carries the old one's second.
        Assert.Equal(1UL, row.SyncSequence);
        var tomb = await context.CalendarTombstones.SingleAsync(None);
        Assert.Equal(cal, tomb.CalendarId);
        Assert.Equal($"{id}.ics", tomb.DavName);
        Assert.Equal(2UL, tomb.SyncSequence);
    }

    /// <summary>
    /// A collection is a property of the whole resource, not of one instance: with a narrow scope
    /// the store used to move the entire series onto the target while carving one occurrence out of
    /// it. Refused instead — the editor's calendar selector is disabled unless the scope is All.
    /// </summary>
    [Theory]
    [InlineData(EditScope.This)]
    [InlineData(EditScope.ThisAndFollowing)]
    public async Task Update_MovingWithANarrowScope_IsRefused(EditScope scope)
    {
        var db = nameof(Update_MovingWithANarrowScope_IsRefused) + scope;
        var (_, user, cal) = await Seed(db);
        var work = (await CalendarStoreTestFactory.Calendars(db).CreateAsync(
            user, new CalendarWrite("Work", null, null, null), "Europe/Brussels", None)).Value;
        var id = (await Events(db).CreateAsync(
            user, Write(cal, repeat: CalendarStoreTestFactory.Weekly()), None)).Value;
        var read = (await Events(db).GetAsync(user, id, None))!;

        var refused = await Events(db).UpdateAsync(
            user, id, scope, "20260921T090000", read.Fields with { CalendarId = work },
            read.IcsHash, None);

        Assert.Equal(CalendarEventStore.MoveNeedsWholeEvent, refused.Error);
        var row = await new PreferencesTestDbContext(db).CalendarEvents.SingleAsync(None);
        Assert.Equal(cal, row.CalendarId);
        Assert.Empty(new PreferencesTestDbContext(db).CalendarRevisions);
    }

    /// <summary>Refused, never truncated: a grid silently missing half its instances is worse than
    /// one told to ask for less. Twelve endless daily series over five years is 21 924 instances.</summary>
    [Fact]
    public async Task Window_OverTheOccurrenceBudget_IsRefused()
    {
        var (db, user, cal) = await Seed(nameof(Window_OverTheOccurrenceBudget_IsRefused));
        var daily = new RecurrenceWrite("DAILY", 1, [], null, null, null, RecurrenceEnd.Never, null, null);
        for (var i = 0; i < 12; i++)
        {
            Assert.True((await Events(db).CreateAsync(
                user, Write(cal, Local(2026, 9, 7, 9), $"Daily {i}", daily), None)).IsSuccess);
        }

        var refused = await Events(db).WindowAsync(
            user, Utc(2026, 9, 1), Utc(2031, 9, 1), "Europe/Brussels", None);

        Assert.Equal(CalendarEventStore.WindowTooDense, refused.Error);
        // The same rows inside a month answer normally: the budget is on the window, not on them.
        Assert.NotEmpty((await Events(db).WindowAsync(
            user, Utc(2026, 9, 1), Utc(2026, 10, 1), "Europe/Brussels", None)).Value);
    }

    /// <summary>A calendar that is not there is not an event that is not there: the two carry
    /// different words so the screen can tell a closed editor from a vanished collection.</summary>
    [Fact]
    public async Task Create_OnACalendarThatIsNotThere_NamesTheCalendar()
    {
        var (db, user, _) = await Seed(nameof(Create_OnACalendarThatIsNotThere_NamesTheCalendar));

        var refused = await Events(db).CreateAsync(user, Write(Guid.NewGuid()), None);

        Assert.Equal(CalendarStore.NotFound, refused.Error);
    }

    /// <summary>Exchange writes a zoned series' RECURRENCE-ID in Z form: 09:00 Brussels on
    /// 14 September is 07:00Z, and both narrow doors must address the same instance through it.</summary>
    [Fact]
    public async Task ARecurrenceIdInZForm_AddressesTheSameInstanceOfAZonedSeries()
    {
        var (db, user, cal) = await Seed(nameof(ARecurrenceIdInZForm_AddressesTheSameInstanceOfAZonedSeries));
        var id = (await Events(db).CreateAsync(
            user, Write(cal, repeat: CalendarStoreTestFactory.Weekly()), None)).Value;
        var read = (await Events(db).GetAsync(user, id, None))!;

        Assert.True((await Events(db).UpdateAsync(
            user, id, EditScope.This, "20260914T070000Z",
            read.Fields with { Start = Local(2026, 9, 14, 11), End = Local(2026, 9, 14, 12) },
            read.IcsHash, None)).IsSuccess);

        var moved = await new PreferencesTestDbContext(db).CalendarEvents.SingleAsync(None);
        // The override is written in the master's own form, whatever form the client addressed it in.
        Assert.Contains("RECURRENCE-ID;TZID=Europe/Brussels:20260914T090000", moved.IcsRaw, StringComparison.Ordinal);

        Assert.True((await Events(db).DeleteAsync(user, id, EditScope.This, "20260914T070000Z", None)).IsSuccess);

        var pruned = await new PreferencesTestDbContext(db).CalendarEvents.SingleAsync(None);
        Assert.Equal(1, Occurrences(pruned.IcsRaw, "BEGIN:VEVENT"));
        Assert.Contains("EXDATE", pruned.IcsRaw, StringComparison.Ordinal);
    }

    /// <summary>An RDATE is an instance like any other: deleting it alone EXDATEs it rather than
    /// touching the rule the series repeats on.</summary>
    [Fact]
    public async Task Delete_This_OnAnRdateOccurrence_ExdatesIt()
    {
        var (db, user, cal) = await Seed(nameof(Delete_This_OnAnRdateOccurrence_ExdatesIt));
        var row = Seeded(user, cal, Ics.Single(
            start: "DTSTART;TZID=Europe/Brussels:20260907T090000",
            end: "DTEND;TZID=Europe/Brussels:20260907T100000",
            extra: "RRULE:FREQ=WEEKLY;COUNT=2\r\nRDATE;TZID=Europe/Brussels:20260920T090000"));
        var seed = new PreferencesTestDbContext(db);
        seed.CalendarEvents.Add(row);
        await seed.SaveChangesAsync(None);

        Assert.True((await Events(db).DeleteAsync(
            user, row.Id, EditScope.This, "20260920T090000", None)).IsSuccess);

        var pruned = await new PreferencesTestDbContext(db).CalendarEvents.SingleAsync(None);
        Assert.Contains("EXDATE;TZID=Europe/Brussels:20260920T090000", pruned.IcsRaw, StringComparison.Ordinal);
        var kept = (await Events(db).WindowAsync(
            user, Utc(2026, 9, 1), Utc(2026, 10, 1), "Europe/Brussels", None)).Value;
        Assert.Equal([7, 14], kept.Select(o => o.StartUtc!.Value.Day));
    }

    /// <summary>A foreign RDATE spelled as a DATE on a timed master names no instance of this
    /// series: a refusal in the composer's words, never an exception out of the store.</summary>
    [Fact]
    public async Task Delete_This_OnAnInstanceIdInTheWrongForm_IsARefusal()
    {
        var (db, user, cal) = await Seed(nameof(Delete_This_OnAnInstanceIdInTheWrongForm_IsARefusal));
        var row = Seeded(user, cal, Ics.Single(
            start: "DTSTART;TZID=Europe/Brussels:20260907T090000",
            end: "DTEND;TZID=Europe/Brussels:20260907T100000",
            extra: "RRULE:FREQ=WEEKLY;COUNT=2\r\nRDATE;VALUE=DATE:20260920"));
        var seed = new PreferencesTestDbContext(db);
        seed.CalendarEvents.Add(row);
        await seed.SaveChangesAsync(None);

        var refused = await Events(db).DeleteAsync(user, row.Id, EditScope.This, "20260920", None);

        Assert.True(refused.IsFailure);
        Assert.Contains("20260920", refused.Error, StringComparison.Ordinal);
        Assert.Single(new PreferencesTestDbContext(db).CalendarEvents);
    }

    [Fact]
    public async Task Delete_All_ArchivesDelete_AndTombstones()
    {
        var (db, user, cal) = await Seed(nameof(Delete_All_ArchivesDelete_AndTombstones));
        var id = (await Events(db).CreateAsync(user, Write(cal), None)).Value;

        Assert.True((await Events(db).DeleteAsync(user, id, EditScope.All, null, None)).IsSuccess);

        var context = new PreferencesTestDbContext(db);
        var revision = await context.CalendarRevisions.SingleAsync(None);
        Assert.Equal(RevisionCause.Delete, revision.Cause);
        // NULL because the revision outlives the row it describes.
        Assert.Null(revision.EventId);
        Assert.Equal(cal, revision.CalendarId);
        Assert.Equal(id.ToString(), revision.Uid);
        Assert.Empty(context.CalendarEvents);
        Assert.Empty(context.CalendarAttendees);
        var tomb = await context.CalendarTombstones.SingleAsync(None);
        Assert.Equal($"{id}.ics", tomb.DavName);
        Assert.Equal(2UL, tomb.SyncSequence);
    }

    // The scope alone decides the whole-delete branch: a row whose ics_raw no longer parses can
    // still be removed under All, which never has to read the document to know it takes everything.
    [Fact]
    public async Task Delete_All_RemovesTheRowEvenWhenItsIcsNoLongerParses()
    {
        var (db, user, cal) = await Seed(nameof(Delete_All_RemovesTheRowEvenWhenItsIcsNoLongerParses));
        var row = Filler(user, cal);
        var seed = new PreferencesTestDbContext(db);
        seed.CalendarEvents.Add(row);
        await seed.SaveChangesAsync(None);

        Assert.True((await Events(db).DeleteAsync(user, row.Id, EditScope.All, null, None)).IsSuccess);

        var context = new PreferencesTestDbContext(db);
        Assert.Empty(context.CalendarEvents);
        var revision = await context.CalendarRevisions.SingleAsync(None);
        Assert.Equal(RevisionCause.Delete, revision.Cause);
        Assert.Null(revision.EventId);
    }

    [Fact]
    public async Task Delete_ThisAndFollowing_KeepsOnlyWhatCameBefore()
    {
        var (db, user, cal) = await Seed(nameof(Delete_ThisAndFollowing_KeepsOnlyWhatCameBefore));
        var id = (await Events(db).CreateAsync(
            user, Write(cal, repeat: CalendarStoreTestFactory.Weekly()), None)).Value;

        Assert.True((await Events(db).DeleteAsync(
            user, id, EditScope.ThisAndFollowing, "20260921T090000", None)).IsSuccess);

        // The split's following half is composed and dropped: one row survives, bounded before the
        // cut, and no second resource is created for the part the user just threw away.
        var row = await new PreferencesTestDbContext(db).CalendarEvents.SingleAsync(None);
        Assert.Equal(RevisionCause.Webmail,
            (await new PreferencesTestDbContext(db).CalendarRevisions.SingleAsync(None)).Cause);
        Assert.Equal(2UL, row.SyncSequence);
        var kept = (await Events(db).WindowAsync(
            user, Utc(2026, 9, 1), Utc(2026, 10, 1), "Europe/Brussels", None)).Value;
        Assert.Equal([7, 14], kept.Select(o => o.StartUtc!.Value.Day));
    }

    [Fact]
    public async Task Window_UsesColumnsToPreselect_ThenExpands()
    {
        var (db, user, cal) = await Seed(nameof(Window_UsesColumnsToPreselect_ThenExpands));
        var inside = (await Events(db).CreateAsync(
            user, Write(cal, Local(2026, 9, 9, 9), "Inside"), None)).Value;
        await Events(db).CreateAsync(user, Write(cal, Local(2026, 10, 20, 9), "Outside"), None);
        var endless = (await Events(db).CreateAsync(
            user, Write(cal, Local(2026, 8, 3, 9), "Endless", CalendarStoreTestFactory.Weekly()), None)).Value;

        var found = (await Events(db).WindowAsync(
            user, Utc(2026, 9, 7), Utc(2026, 9, 14), "Europe/Brussels", None)).Value;

        Assert.Equal(
            new[] { inside, endless }.Order(), found.Select(o => o.EventId).Distinct().Order());
        Assert.DoesNotContain(found, o => o.Summary == "Outside");
        // Sorted across rows: the client places by time, and two rows interleave.
        Assert.Equal(found.Select(o => o.StartUtc), found.Select(o => o.StartUtc).Order());
    }

    /// <summary>
    /// A calendar twelve hours ahead of UTC stores an all-day event's instants half a day before
    /// the dates it names, so the row falls outside a bare <c>last_occurrence &gt; from</c> while
    /// the day it covers is squarely inside the window. The margin preselects it; the expander,
    /// which alone reads the dates, decides that it belongs.
    /// </summary>
    [Fact]
    public async Task Window_KeepsAnAllDayRowThatOnlyTheMarginPreselects()
    {
        var db = nameof(Window_KeepsAnAllDayRowThatOnlyTheMarginPreselects);
        var user = Guid.NewGuid();
        var far = (await CalendarStoreTestFactory.Calendars(db).CreateAsync(
            user, new CalendarWrite("Trips", null, null, null), "Pacific/Auckland", None)).Value;
        await Events(db).CreateAsync(
            user, CalendarStoreTestFactory.AllDay(far, new DateOnly(2026, 9, 6)), None);

        var row = await new PreferencesTestDbContext(db).CalendarEvents.SingleAsync(None);
        Assert.True(row.LastOccurrence < new DateTime(2026, 9, 6, 18, 0, 0, DateTimeKind.Utc));

        var found = (await Events(db).WindowAsync(
            user, new DateTime(2026, 9, 6, 18, 0, 0, DateTimeKind.Utc), Utc(2026, 9, 8),
            "Pacific/Auckland", None)).Value;

        var only = Assert.Single(found);
        Assert.True(only.IsAllDay);
        Assert.Equal(new DateOnly(2026, 9, 6), only.StartDate);
    }

    [Fact]
    public async Task Create_RefusesTheFiveThousandAndFirst()
    {
        var (db, user, cal) = await Seed(nameof(Create_RefusesTheFiveThousandAndFirst));
        var seed = new PreferencesTestDbContext(db);
        seed.CalendarEvents.AddRange(Enumerable.Range(0, CalendarEventStore.MaxPerCalendar)
            .Select(_ => Filler(user, cal)));
        await seed.SaveChangesAsync(None);

        var refused = await Events(db).CreateAsync(user, Write(cal), None);

        Assert.Equal(CalendarEventStore.CapReached, refused.Error);
    }

    [Fact]
    public async Task Search_OneResultPerEvent_AtNextOccurrence()
    {
        var (db, user, cal) = await Seed(nameof(Search_OneResultPerEvent_AtNextOccurrence));
        var now = DateTime.UtcNow;
        await Events(db).CreateAsync(
            user, Write(cal, Wall(now.Date.AddDays(3).AddHours(9)), "Sprint standup",
                CalendarStoreTestFactory.Weekly()), None);
        await Events(db).CreateAsync(
            user, Write(cal, Wall(now.Date.AddDays(-30).AddHours(9)), "Sprint retro"), None);
        await Events(db).CreateAsync(user, Write(cal, Wall(now.Date.AddDays(3).AddHours(14)), "Lunch"), None);

        var found = await Events(db).SearchAsync(user, "sprint", None);

        // One result per event, never one per instance: a weekly standup would otherwise fill the
        // whole list on its own.
        Assert.Equal(2, found.Count);
        Assert.True(found.Single(o => o.Summary == "Sprint standup").StartUtc > now);
        // A series already over answers with the last occurrence it ever had.
        Assert.True(found.Single(o => o.Summary == "Sprint retro").StartUtc < now);
    }

    [Fact]
    public async Task Search_TreatsThePercentSignAsText()
    {
        var (db, user, cal) = await Seed(nameof(Search_TreatsThePercentSignAsText));
        await Events(db).CreateAsync(user, Write(cal, summary: "Budget 100% done"), None);
        await Events(db).CreateAsync(user, Write(cal, Local(2026, 9, 8, 9), "Nothing to do with it"), None);

        Assert.Single(await Events(db).SearchAsync(user, "100%", None));
        // Escaped, not stripped: a bare % would have matched every row of the collection.
        Assert.Empty(await Events(db).SearchAsync(user, "1%0", None));
    }

    [Fact]
    public async Task Get_AnswersNothingForAnotherUsersEvent()
    {
        var (db, user, cal) = await Seed(nameof(Get_AnswersNothingForAnotherUsersEvent));
        var id = (await Events(db).CreateAsync(user, Write(cal), None)).Value;

        Assert.Null(await Events(db).GetAsync(Guid.NewGuid(), id, None));
        Assert.Equal(CalendarEventStore.NotFound,
            (await Events(db).DeleteAsync(Guid.NewGuid(), id, EditScope.All, null, None)).Error);
    }

    private static int Occurrences(string text, string needle) =>
        text.Split(needle).Length - 1;

    private static Task<(string Db, Guid User, Guid Calendar)> Seed(string db) =>
        CalendarStoreTestFactory.SeedAsync(db);

    private static CalendarEventStore Events(string db) => CalendarStoreTestFactory.Events(db);

    private static EventWrite Write(
        Guid cal, DateTime? start = null, string? summary = "Standup", RecurrenceWrite? repeat = null) =>
        CalendarStoreTestFactory.Write(cal, start, summary, repeat);

    private static DateTime Local(int y, int m, int d, int h) =>
        CalendarStoreTestFactory.Local(y, m, d, h);

    private static DateTime Wall(DateTime at) => CalendarStoreTestFactory.Wall(at);

    private static DateTime Utc(int y, int m, int d) => CalendarStoreTestFactory.Utc(y, m, d);

    /// <summary>A resource seeded verbatim, for the shapes the editor cannot compose — an RDATE,
    /// a foreign spelling — read back through the store's own doors.</summary>
    private static CalendarEvent Seeded(Guid user, Guid calendar, string ics)
    {
        var id = Guid.NewGuid();
        return new CalendarEvent
        {
            Id = id, CalendarId = calendar, UserId = user, Uid = "single", DavName = $"{id}.ics",
            IcsRaw = ics, IcsHash = IcsDocument.HashOf(ics),
            FirstOccurrence = CalendarStoreTestFactory.Utc(2026, 9, 7),
            LastOccurrence = CalendarStoreTestFactory.Utc(2026, 9, 21)
        };
    }

    /// <summary>A row that only has to exist, to reach the ceiling without composing five thousand
    /// resources through the editor.</summary>
    private static CalendarEvent Filler(Guid user, Guid calendar)
    {
        var id = Guid.NewGuid();
        return new CalendarEvent
        {
            Id = id, CalendarId = calendar, UserId = user, Uid = id.ToString(),
            DavName = $"{id}.ics", IcsRaw = string.Empty, IcsHash = string.Empty
        };
    }
}
