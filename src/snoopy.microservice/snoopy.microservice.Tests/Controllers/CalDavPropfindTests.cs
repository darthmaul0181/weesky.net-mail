using System.Xml.Linq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using CalendarRow = weesky.Snoopy.Microservice.Data.Preferences.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class CalDavPropfindTests : IAsyncLifetime
{
    private static readonly Guid Epoch = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private DavTestServer server = null!;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync() => server = await DavTestServer.StartAsync();

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task TheHomeOfAnAccountWithNoCalendar_AnswersItselfAlone()
    {
        var response = await Propfind(DavPaths.CalendarHome(UserId), "1", PropBody("resourcetype"));

        // A hand-restored base holds no calendar (cadrage, décision 6): the list is empty, and
        // nothing invents a `default` in a guessed zone.
        Assert.Equal(207, response.StatusCode);
        Assert.Equal([DavPaths.CalendarHome(UserId)], HrefsOf(response));
    }

    [Fact]
    public async Task TheHome_ListsEveryCalendarInSidebarOrder()
    {
        GivenCalendars(("work", 2), ("home", 1), ("misc", 3));

        var response = await Propfind(DavPaths.CalendarHome(UserId), "1", PropBody("displayname"));

        // Under the EXACT href a client will PROPFIND next: a member href built any other way 404s
        // on every cycle, so the construction is pinned and not merely the count.
        Assert.Equal(
        [
            DavPaths.CalendarHome(UserId), DavPaths.Calendar(UserId, "home"),
            DavPaths.Calendar(UserId, "work"), DavPaths.Calendar(UserId, "misc"),
        ], HrefsOf(response));
    }

    [Fact]
    public async Task TheHome_ListsTheHiddenCalendarsToo()
    {
        GivenCalendars(("work", 1));
        GivenAHiddenCalendar("secret");

        var response = await Propfind(DavPaths.CalendarHome(UserId), "1", PropBody("displayname"));

        Assert.Equal(3, HrefsOf(response).Count);
    }

    [Fact]
    public async Task TheCollectionOfHomes_ListsTheOneHome()
    {
        var response = await Propfind(DavPaths.CalendarCollection, "1", PropBody("resourcetype"));

        Assert.Equal([DavPaths.CalendarCollection, DavPaths.CalendarHome(UserId)], HrefsOf(response));
    }

    [Fact]
    public async Task DepthInfinityOnACollection_IsRefused()
    {
        GivenCalendars(("work", 1));

        foreach (var path in new[]
                 {
                     DavPaths.CalendarCollection, DavPaths.CalendarHome(UserId),
                     DavPaths.Calendar(UserId, "work"),
                 })
        {
            var response = await Propfind(path, "infinity", PropBody("resourcetype"));

            Assert.Equal(403, response.StatusCode);
            Assert.Equal(DavXml.Dav + "propfind-finite-depth", ConditionOf(response));
        }
    }

    [Fact]
    public async Task AnUnknownCalendar_Answers404()
    {
        var response = await Propfind(DavPaths.Calendar(UserId, "nowhere"), "0", PropBody("displayname"));

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task ACalendarNameThisCollectionWillNotHold_Answers404()
    {
        var response = await Propfind(
            $"{DavPaths.CalendarHome(UserId)}{new string('a', 256)}/", "0", PropBody("displayname"));

        // The same 404 an unknown name gets: a name we will not hold designates nothing.
        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task AForeignUserId_Answers404()
    {
        var response = await Propfind(DavPaths.CalendarHome(Guid.NewGuid()), "0", PropBody("resourcetype"));

        // 403 would confirm the principal aimed at exists.
        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task ACalendarAtDepthOne_AnswersItselfThenOneResponsePerEvent()
    {
        var calendar = GivenCalendars(("work", 1))[0];
        GivenEvents(calendar, ("a.ics", 1UL), ("plan #9.ics", 2UL));

        var response = await Propfind(DavPaths.Calendar(UserId, "work"), "1", PropBody("getetag"));

        Assert.Equal(
        [
            DavPaths.Calendar(UserId, "work"), DavPaths.Event(UserId, "work", "a.ics"),
            DavPaths.Event(UserId, "work", "plan #9.ics"),
        ], HrefsOf(response));
        Assert.Equal("\"hash-a.ics\"", EtagOf(response, DavPaths.Event(UserId, "work", "a.ics")));
    }

    [Fact]
    public async Task ACalendarAtDepthOne_BoundsItsMembersToTheCounterItRead()
    {
        var calendar = GivenCalendars(("work", 1))[0];
        GivenTheCounterAt(calendar, 20);
        GivenEvents(calendar, ("a.ics", 5UL), ("late.ics", 25UL));

        var response = await Propfind(DavPaths.Calendar(UserId, "work"), "1", PropBody("getetag"));

        // The counter before the members: a row committed in between would be covered by the
        // returned ctag without ever appearing in the list, and a client reads absence as a delete.
        Assert.DoesNotContain(HrefsOf(response), href => href.EndsWith("late.ics"));
        Assert.Contains(HrefsOf(response), href => href.EndsWith("a.ics"));
    }

    [Fact]
    public async Task ACalendarAtDepthZero_AnswersTheCalendarAlone()
    {
        var calendar = GivenCalendars(("work", 1))[0];
        GivenEvents(calendar, ("a.ics", 1UL));

        var response = await Propfind(DavPaths.Calendar(UserId, "work"), "0", PropBody("displayname"));

        Assert.Equal([DavPaths.Calendar(UserId, "work")], HrefsOf(response));
    }

    [Fact]
    public async Task AnEvent_AnswersItsOwnProperties()
    {
        var calendar = GivenCalendars(("work", 1))[0];
        GivenEvents(calendar, ("a.ics", 1UL));

        var response = await Propfind(
            DavPaths.Event(UserId, "work", "a.ics"), "0", PropBody("getetag"));

        Assert.Equal(207, response.StatusCode);
        Assert.Equal("\"hash-a.ics\"", EtagOf(response, DavPaths.Event(UserId, "work", "a.ics")));
    }

    [Fact]
    public async Task AnUnknownEvent_Answers404()
    {
        GivenCalendars(("work", 1));

        var response = await Propfind(
            DavPaths.Event(UserId, "work", "nowhere.ics"), "0", PropBody("getetag"));

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task ACalendarWithAnEncodedName_IsServedUnderThatSameHref()
    {
        var calendar = GivenCalendars(("Mes vacances", 1))[0];
        GivenEvents(calendar, ("a.ics", 1UL));

        var response = await Propfind(
            DavPaths.Calendar(UserId, "Mes vacances"), "1", PropBody("resourcetype"));

        Assert.Equal(207, response.StatusCode);
        Assert.Equal(
        [
            DavPaths.Calendar(UserId, "Mes vacances"),
            DavPaths.Event(UserId, "Mes vacances", "a.ics"),
        ], HrefsOf(response));
    }

    private Task<DavTestResponse> Propfind(string path, string? depth, string? body) =>
        server.PropfindAsync(path, depth, body);

    private CalendarRow[] GivenCalendars(params (string Name, int Order)[] calendars)
    {
        using var db = server.CreateContext();
        var rows = calendars.Select(c => NewCalendar(c.Name, c.Order)).ToArray();
        db.Calendars.AddRange(rows);
        db.SaveChanges();
        return rows;
    }

    private void GivenAHiddenCalendar(string davName)
    {
        using var db = server.CreateContext();
        var row = NewCalendar(davName, 9);
        row.IsVisible = false;
        db.Calendars.Add(row);
        db.SaveChanges();
    }

    private void GivenEvents(CalendarRow calendar, params (string Name, ulong Rank)[] events)
    {
        using var db = server.CreateContext();
        foreach (var (name, rank) in events)
        {
            db.CalendarEvents.Add(new CalendarEvent
            {
                Id = Guid.NewGuid(),
                CalendarId = calendar.Id,
                UserId = UserId,
                Uid = Guid.NewGuid().ToString(),
                DavName = name,
                StartsAt = new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
                EndsAt = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc),
                FirstOccurrence = new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
                LastOccurrence = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc),
                Transparency = "OPAQUE",
                IcsRaw = "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n",
                IcsHash = $"hash-{name}",
                SyncSequence = rank,
                UpdatedAt = new DateTime(2026, 9, 7, 6, 0, 0, DateTimeKind.Utc),
            });
        }

        db.SaveChanges();
    }

    private void GivenTheCounterAt(CalendarRow calendar, ulong seq)
    {
        using var db = server.CreateContext();
        db.CalendarSyncStates.Add(new CalendarSyncState
        {
            CalendarId = calendar.Id, Epoch = Epoch, Seq = seq, PrunedBelow = 0,
        });
        db.SaveChanges();
    }

    private CalendarRow NewCalendar(string davName, int order) => new()
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

    private static string PropBody(params string[] names) =>
        new XElement(DavXml.Dav + "propfind",
            new XElement(DavXml.Prop, names.Select(name => new XElement(DavXml.Dav + name))))
            .ToString();

    private static XName ConditionOf(DavTestResponse response) =>
        XDocument.Parse(response.Body).Root!.Elements().First().Name;

    private static List<XElement> ResponsesOf(DavTestResponse response) =>
        [.. XDocument.Parse(response.Body).Descendants(DavXml.Response)];

    private static List<string> HrefsOf(DavTestResponse response) =>
        [.. ResponsesOf(response).Select(r => r.Element(DavXml.Href)!.Value)];

    /// <summary>Read off the response naming that href: the collection carries a getetag of its own,
    /// empty, in the 404 propstat.</summary>
    private static string? EtagOf(DavTestResponse response, string href) =>
        ResponsesOf(response)
            .Single(r => r.Element(DavXml.Href)!.Value == href)
            .Descendants(DavXml.Dav + "getetag").Single().Value;
}
