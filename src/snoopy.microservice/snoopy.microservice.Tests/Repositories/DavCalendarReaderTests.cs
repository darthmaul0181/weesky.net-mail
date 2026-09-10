using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using CalendarRow = weesky.Snoopy.Microservice.Data.Preferences.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class DavCalendarReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    [Fact]
    public async Task ItListsBySortOrderThenName()
    {
        var reader = ReaderOver(
            Calendar("work", order: 2), Calendar("beta", order: 1), Calendar("alpha", order: 1));

        var calendars = await reader.ListAsync(UserId, CancellationToken.None);

        Assert.Equal(["alpha", "beta", "work"], calendars.Select(c => c.DavName));
    }

    [Fact]
    public async Task ItListsTheHiddenOnesToo()
    {
        // is_visible is a sidebar checkbox, never projected to DAV (cadrage, décision 2): a client
        // that stopped receiving a calendar because a box was unticked would delete its copy.
        var hidden = Calendar("secret");
        hidden.IsVisible = false;
        var reader = ReaderOver(hidden);

        Assert.Single(await reader.ListAsync(UserId, CancellationToken.None));
    }

    [Fact]
    public async Task AnotherUsersCalendar_IsNotFound()
    {
        var foreign = Calendar("work");
        foreign.UserId = Other;
        var reader = ReaderOver(foreign);

        Assert.Null(await reader.FindCalendarAsync(UserId, "work", CancellationToken.None));
        Assert.Empty(await reader.ListAsync(UserId, CancellationToken.None));
    }

    [Fact]
    public async Task ItFindsOneCalendarByItsName()
    {
        var reader = ReaderOver(Calendar("work"), Calendar("home"));

        var found = await reader.FindCalendarAsync(UserId, "home", CancellationToken.None);

        Assert.Equal("home", found?.DavName);
        Assert.Equal("Europe/Brussels", found?.TimeZone);
    }

    [Fact]
    public async Task AnEventOfAnotherCalendar_IsNotFound()
    {
        var mine = Calendar("work");
        var theirs = Calendar("home");
        var reader = ReaderOver([mine, theirs], Event(theirs.Id, "a.ics"));

        Assert.Null(await reader.FindAsync(mine.Id, "a.ics", CancellationToken.None));
        Assert.Empty(await reader.FindManyAsync(mine.Id, ["a.ics"], CancellationToken.None));
    }

    [Fact]
    public async Task ItStreamsUpToTheBoundItIsGiven()
    {
        var calendar = Calendar("work");
        var reader = ReaderOver(
            [calendar],
            Event(calendar.Id, "early.ics", rank: 5), Event(calendar.Id, "late.ics", rank: 25));

        var names = await Collect(reader.StreamAsync(calendar.Id, 20, CancellationToken.None));

        // The counter the ctag was cut from bounds the members, or the answer covers a write it
        // does not carry.
        Assert.Equal(["early.ics"], names);
    }

    [Fact]
    public async Task ItStreamsTheChangesOfOneWindowInRankOrder()
    {
        var calendar = Calendar("work");
        var reader = ReaderOver(
            [calendar],
            Event(calendar.Id, "c.ics", rank: 30), Event(calendar.Id, "a.ics", rank: 10),
            Event(calendar.Id, "b.ics", rank: 20));

        var names = await Collect(reader.ChangedAsync(calendar.Id, 10, 25, CancellationToken.None));

        Assert.Equal(["b.ics"], names);
    }

    [Fact]
    public async Task ItReadsTheTombstonesOfOneWindow()
    {
        var calendar = Calendar("work");
        using var context = ContextOver([calendar]);
        context.CalendarTombstones.AddRange(
            new CalendarTombstone { CalendarId = calendar.Id, DavName = "gone.ics", SyncSequence = 7 },
            new CalendarTombstone { CalendarId = calendar.Id, DavName = "older.ics", SyncSequence = 2 });
        await context.SaveChangesAsync();

        var tombstones = await new DavCalendarReader(context)
            .TombstonesAsync(calendar.Id, 5, 9, CancellationToken.None);

        Assert.Equal("gone.ics", Assert.Single(tombstones).DavName);
    }

    [Fact]
    public async Task ItCountsWhatTheCollectionHolds()
    {
        var calendar = Calendar("work");
        var reader = ReaderOver([calendar], Event(calendar.Id, "a.ics"), Event(calendar.Id, "b.ics"));

        Assert.Equal(2, await reader.CountAsync(calendar.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Candidates_AreFilteredByEachColumnAsked()
    {
        var calendar = Calendar("work");
        var cancelled = Event(calendar.Id, "off.ics");
        cancelled.Status = "CANCELLED";
        var reader = ReaderOver([calendar], Event(calendar.Id, "on.ics"), cancelled);

        var confirmed = await Collect(reader.CandidatesAsync(
            calendar.Id, null, null, new EventColumnFilter("CANCELLED", null, null), ulong.MaxValue,
            CancellationToken.None));

        Assert.Equal(["off.ics"], confirmed);
    }

    [Fact]
    public async Task Candidates_NeverPreselectARecurringRowOnItsColumns()
    {
        // The columns hold the master's values and a prop-filter is satisfied by any component:
        // a series whose override alone is CANCELLED must reach the file that will say so.
        var calendar = Calendar("work");
        var series = Event(calendar.Id, "series.ics");
        series.IsRecurring = true;
        var reader = ReaderOver([calendar], Event(calendar.Id, "plain.ics"), series);

        var found = await Collect(reader.CandidatesAsync(
            calendar.Id, null, null, new EventColumnFilter("CANCELLED", "TRANSPARENT", "PRIVATE"), ulong.MaxValue,
            CancellationToken.None));

        Assert.Equal(["series.ics"], found);
    }

    [Fact]
    public async Task Candidates_AreBoundedByTheWindowAndByTheCounter()
    {
        var calendar = Calendar("work");
        var far = Event(calendar.Id, "far.ics");
        far.FirstOccurrence = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        far.LastOccurrence = new DateTime(2030, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var late = Event(calendar.Id, "late.ics", rank: 40);
        var reader = ReaderOver([calendar], Event(calendar.Id, "near.ics"), far, late);

        var found = await Collect(reader.CandidatesAsync(
            calendar.Id, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc), EventColumnFilter.None, 20,
            CancellationToken.None));

        Assert.Equal(["near.ics"], found);
    }

    [Fact]
    public async Task Candidates_KeepTheDayOfSlackTheWindowQueryApplies()
    {
        // The case DavCalendarReader.Slack is written for: a calendar in Pacific/Auckland, an
        // all-day 2 January. The columns hold
        // [01T11:00Z, 02T11:00Z] because the day is placed in the calendar's zone, while the
        // expander reads the instance as [02T00:00Z, 03T00:00Z[ on dates. Bare bounds would drop
        // the candidate before any expansion judged it, and the calendar-query would answer false
        // with nothing in the logs.
        var calendar = Calendar("work");
        calendar.TimeZone = "Pacific/Auckland";
        var allDay = Event(calendar.Id, "chores.ics");
        allDay.IsAllDay = true;
        allDay.FirstOccurrence = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        allDay.LastOccurrence = new DateTime(2026, 1, 2, 11, 0, 0, DateTimeKind.Utc);
        var reader = ReaderOver([calendar], allDay);

        var found = await Collect(reader.CandidatesAsync(
            calendar.Id, new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), EventColumnFilter.None,
            ulong.MaxValue, CancellationToken.None));

        Assert.Equal(["chores.ics"], found);
    }

    [Fact]
    public async Task Candidates_KeepARowTheRequestsZoneMovesTwentySixHoursAwayFromItsColumns()
    {
        // The widest spread two zones can have: a calendar in Kiritimati (UTC+14) holds an all-day
        // 17 October as [16T10:00Z, 17T10:00Z]; a request in Pago Pago (UTC-11) reads that day as
        // [17T11:00Z, 18T11:00Z[ and asks for its last half hour. A day of slack drops the row; the
        // preselection must reach 26 hours back — and no further, or the band would be a blanket.
        var calendar = Calendar("work");
        calendar.TimeZone = "Pacific/Kiritimati";
        var allDay = Event(calendar.Id, "journee.ics");
        allDay.IsAllDay = true;
        allDay.FirstOccurrence = new DateTime(2028, 10, 16, 10, 0, 0, DateTimeKind.Utc);
        allDay.LastOccurrence = new DateTime(2028, 10, 17, 10, 0, 0, DateTimeKind.Utc);
        var reader = ReaderOver([calendar], allDay);
        var to = new DateTime(2028, 10, 18, 11, 0, 0, DateTimeKind.Utc);

        var lastHalfHour = await Collect(reader.CandidatesAsync(calendar.Id,
            new DateTime(2028, 10, 18, 10, 30, 0, DateTimeKind.Utc), to, EventColumnFilter.None,
            ulong.MaxValue, CancellationToken.None));
        var pastTheSpread = await Collect(reader.CandidatesAsync(calendar.Id,
            new DateTime(2028, 10, 18, 12, 0, 0, DateTimeKind.Utc), to.AddHours(1), EventColumnFilter.None,
            ulong.MaxValue, CancellationToken.None));

        Assert.Equal(["journee.ics"], lastHalfHour);
        Assert.Empty(pastTheSpread);
    }

    private static async Task<List<string>> Collect(IAsyncEnumerable<DavEvent> events)
    {
        List<string> names = [];
        await foreach (var found in events) names.Add(found.DavName);
        return names;
    }

    private static DavCalendarReader ReaderOver(params CalendarRow[] calendars) =>
        new(ContextOver(calendars));

    private static DavCalendarReader ReaderOver(CalendarRow[] calendars, params CalendarEvent[] events) =>
        new(ContextOver(calendars, events));

    private static PreferencesTestDbContext ContextOver(
        CalendarRow[] calendars, params CalendarEvent[] events)
    {
        var context = new PreferencesTestDbContext(Guid.NewGuid().ToString());
        context.Calendars.AddRange(calendars);
        context.CalendarEvents.AddRange(events);
        context.SaveChanges();
        return context;
    }

    private static CalendarRow Calendar(string davName, int order = 0) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        DavName = davName,
        DisplayName = davName,
        Description = string.Empty,
        Color = "#336699",
        Order = order,
        TimeZone = "Europe/Brussels",
        IsVisible = true,
    };

    private static CalendarEvent Event(Guid calendarId, string davName, ulong rank = 1) => new()
    {
        Id = Guid.NewGuid(),
        CalendarId = calendarId,
        UserId = UserId,
        Uid = Guid.NewGuid().ToString(),
        DavName = davName,
        StartsAt = new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
        EndsAt = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc),
        FirstOccurrence = new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
        LastOccurrence = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc),
        Transparency = "OPAQUE",
        IcsRaw = "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n",
        IcsHash = $"hash-{davName}",
        SyncSequence = rank,
        UpdatedAt = new DateTime(2026, 9, 7, 6, 0, 0, DateTimeKind.Utc),
    };
}
