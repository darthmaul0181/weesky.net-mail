using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class CalendarEventStoreImportTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task Import_GroupsByUid_ReplacesExisting_IgnoresTodos_CountsFailures()
    {
        var (db, user, cal) = await Seed(
            nameof(Import_GroupsByUid_ReplacesExisting_IgnoresTodos_CountsFailures));

        var first = await Events(db).ImportAsync(user, cal, Ics.GoogleLikeExport(), None);

        // Two UIDs make two resources however many components they hold; the VTODO is counted, not
        // stored; the VEVENT with no DTSTART is the one refusal.
        Assert.Equal(
            (2, 0, 1, 0, 1),
            (first.Created, first.Replaced, first.IgnoredTodos, first.IgnoredJournals, first.Failed));
        Assert.Equal(2, await new PreferencesTestDbContext(db).CalendarEvents.CountAsync(None));

        var again = await Events(db).ImportAsync(user, cal, Ics.GoogleLikeExport(), None);

        Assert.Equal((0, 2), (again.Created, again.Replaced));
        Assert.Equal(2, await new PreferencesTestDbContext(db).CalendarRevisions
            .CountAsync(r => r.Cause == RevisionCause.Import, None));
        // Replaced whole, never merged: the file's resource IS the event.
        Assert.Equal(2, await new PreferencesTestDbContext(db).CalendarEvents.CountAsync(None));
    }

    [Fact]
    public async Task Import_KeepsEveryComponentOfOneUid()
    {
        var (db, user, cal) = await Seed(nameof(Import_KeepsEveryComponentOfOneUid));

        await Events(db).ImportAsync(user, cal, Ics.GoogleLikeExport(), None);

        var series = await new PreferencesTestDbContext(db).CalendarEvents
            .SingleAsync(e => e.Uid == "standup@google.com", None);
        // The master and its two overrides live in ONE resource; splitting them would make each
        // exception a separate event nothing ties back to the series.
        Assert.Equal(3, series.IcsRaw.Split("BEGIN:VEVENT").Length - 1);
        Assert.True(series.IsRecurring);
    }

    [Fact]
    public async Task Import_InsertsMissingUid_AndCapsAtTwentyMegabytes()
    {
        var (db, user, cal) = await Seed(nameof(Import_InsertsMissingUid_AndCapsAtTwentyMegabytes));

        var outcome = await Events(db).ImportAsync(user, cal, Ics.EventWithoutUid(), None);

        Assert.Equal(1, outcome.Created);
        var row = await new PreferencesTestDbContext(db).CalendarEvents.SingleAsync(None);
        // Every stored resource carries the identity a CalDAV client syncs on, whatever the file
        // brought — and it carries it in its BYTES, which is what the ETag is cut from.
        Assert.NotEqual(string.Empty, row.Uid);
        Assert.Contains("UID:", row.IcsRaw, StringComparison.Ordinal);

        var oversized = new string('x', CalendarEventStore.MaxImportBytes + 1);
        var refused = await Events(db).ImportAsync(user, cal, oversized, None);

        // Judged before a single byte is parsed: parsing is the work an oversized body wants.
        Assert.Equal(1, refused.Failed);
        Assert.Equal(0, refused.Created);
        Assert.Equal(CalendarEventStore.FileTooLarge, Assert.Single(refused.Errors).Reason);
    }

    [Fact]
    public void WithUid_InsertsOnlyWhereTheComponentDeclaresNone()
    {
        var inserted = CalendarEventImporter.WithUid(Ics.EventWithoutUid(), "chosen");

        Assert.Contains("BEGIN:VEVENT\r\nUID:chosen\r\n", inserted, StringComparison.Ordinal);

        var already = Ics.Single(start: "DTSTART:20260907T090000Z", end: null);
        // Replacing a UID a resource already carries would rotate the identity every client syncs on.
        Assert.Equal(already, CalendarEventImporter.WithUid(already, "chosen"));
    }

    [Fact]
    public async Task Import_RefusesADensityBomb()
    {
        var (db, user, cal) = await Seed(nameof(Import_RefusesADensityBomb));

        var outcome = await Events(db).ImportAsync(user, cal, Ics.DensityBomb(), None);

        // The webmail editor cannot express FREQ=MINUTELY at all, so the density gate is reachable
        // only from here — which is exactly why the import runs it.
        Assert.Equal(1, outcome.Failed);
        Assert.Empty(new PreferencesTestDbContext(db).CalendarEvents);
        Assert.Contains("instances", Assert.Single(outcome.Errors).Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The engine is total and silent by design — it has no logger — so the store is the only
    /// layer that can say a TZID resolved to nothing. Stored anyway: the file's own VTIMEZONE
    /// answered the instants, and refusing the resource would lose data over a naming quarrel.
    /// </summary>
    [Fact]
    public async Task Import_LogsATimeZoneNothingResolves()
    {
        var (db, user, cal) = await Seed(nameof(Import_LogsATimeZoneNothingResolves));
        var logger = new Mock<ILogger<CalendarEventStore>>();

        var outcome = await CalendarStoreTestFactory.Events(db, logger.Object).ImportAsync(
            user, cal,
            Ics.Single(start: "DTSTART;TZID=Custom/Nowhere:20260907T090000", end: null,
                zone: Ics.FixedZone("Custom/Nowhere", "+0300")),
            None);

        Assert.Equal(1, outcome.Created);
        logger.VerifyWarningLoggedContaining("Custom/Nowhere");
    }

    [Fact]
    public async Task Export_IsOneVcalendar_WithDedupedZones_AndCalendarName()
    {
        var (db, user, cal) = await Seed(nameof(Export_IsOneVcalendar_WithDedupedZones_AndCalendarName));
        await Events(db).CreateAsync(user, CalendarStoreTestFactory.Write(cal), None);
        await Events(db).CreateAsync(
            user, CalendarStoreTestFactory.Write(cal, CalendarStoreTestFactory.Local(2026, 9, 8, 9)), None);

        var text = await Events(db).ExportAsync(user, cal, None);

        Assert.Single(Regex.Matches(text, "BEGIN:VCALENDAR"));
        // Both events cite Europe/Brussels; a block per resource is a file three times its size.
        Assert.Single(Regex.Matches(text, "BEGIN:VTIMEZONE"));
        Assert.Equal(2, Regex.Matches(text, "BEGIN:VEVENT").Count);
        Assert.Contains("X-WR-CALNAME:Personal", text, StringComparison.Ordinal);
        Assert.Contains("NAME:Personal", text, StringComparison.Ordinal);
        Assert.Contains("COLOR:", text, StringComparison.Ordinal);

        var reimported = await Events(db).ImportAsync(user, cal, text, None);

        // Our own file round-trips onto the very resources it came from: same UIDs, no duplicates.
        Assert.Equal((0, 2, 0), (reimported.Created, reimported.Replaced, reimported.Failed));
    }

    /// <summary>A collection belonging to somebody else is indistinguishable from one that does not
    /// exist — the same reading every other door of these two stores gives.</summary>
    [Fact]
    public async Task Import_IntoAnotherUsersCalendar_IsNotFound()
    {
        var (db, _, cal) = await Seed(nameof(Import_IntoAnotherUsersCalendar_IsNotFound));

        var outcome = await Events(db).ImportAsync(Guid.NewGuid(), cal, Ics.GoogleLikeExport(), None);

        Assert.Equal((0, 0, 1), (outcome.Created, outcome.Replaced, outcome.Failed));
        Assert.Equal(CalendarStore.NotFound, Assert.Single(outcome.Errors).Reason);
        Assert.Empty(new PreferencesTestDbContext(db).CalendarEvents);
    }

    [Fact]
    public async Task Import_OfATodoOnlyFile_CountsItAndStoresNothing()
    {
        var (db, user, cal) = await Seed(nameof(Import_OfATodoOnlyFile_CountsItAndStoresNothing));

        var outcome = await Events(db).ImportAsync(user, cal, Ics.Todo(), None);

        Assert.Equal((0, 1, 0), (outcome.Created, outcome.IgnoredTodos, outcome.Failed));
        Assert.Empty(new PreferencesTestDbContext(db).CalendarEvents);
    }

    private static Task<(string Db, Guid User, Guid Calendar)> Seed(string db) =>
        CalendarStoreTestFactory.SeedAsync(db);

    private static CalendarEventStore Events(string db) => CalendarStoreTestFactory.Events(db);
}
