using System.Xml.Linq;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.CalDav;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Services.CalDav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using CalendarRow = weesky.Snoopy.Microservice.Data.Preferences.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// Creating a calendar from a client (§ 11), through both doors: MKCALENDAR and the extended
/// MKCOL. Every row of the table is answered here, on both verbs where both serve it.
/// </summary>
public sealed class CalDavMkcalendarTests : IAsyncLifetime
{
    private DavTestServer server = null!;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync();
        GivenTheDefaultCalendar();
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Theory]
    [InlineData("MKCALENDAR")]
    [InlineData("MKCOL")]
    public async Task ACreationOnAFreeSegment_Answers201NoCache(string method)
    {
        var response = await Create(method, "trips", Displayname("Trips"));

        Assert.Equal(201, response.StatusCode);
        Assert.Equal(DavHeaders.NoCache, response.Header("Cache-Control"));
        Assert.Equal(DavHeaders.ComplianceClasses, response.Header("DAV"));
    }

    [Theory]
    [InlineData("MKCALENDAR")]
    [InlineData("MKCOL")]
    public async Task WhatWasCreated_IsWhatTheHomeListsNext(string method)
    {
        Assert.Equal(201, (await Create(method, "trips",
            Displayname("Trips"),
            new XElement(DavXml.CalDav + "calendar-description", "Away"),
            new XElement(DavXml.Apple + "calendar-color", "#FF0000FF"),
            new XElement(DavXml.Apple + "calendar-order", "7"),
            new XElement(DavXml.CalDav + "calendar-timezone",
                MkCalendarRequestTests.Zones("Pacific/Auckland")))).StatusCode);

        var listed = await Propfind(DavPaths.Calendar(UserId, "trips"), "displayname",
            "calendar-description");

        Assert.Equal(207, listed.StatusCode);
        var found = XDocument.Parse(listed.Body).Descendants(DavXml.Prop).First();
        Assert.Equal("Trips", found.Element(DavXml.Dav + "displayname")!.Value);
        Assert.Equal("Away", found.Element(DavXml.CalDav + "calendar-description")!.Value);

        using var db = server.CreateContext();
        var row = db.Calendars.Single(c => c.DavName == "trips");
        Assert.Equal("#ff0000", row.Color);
        Assert.Equal(7, row.Order);
        Assert.Equal("Pacific/Auckland", row.TimeZone);
        Assert.NotNull(db.CalendarSyncStates.Find(row.Id));
    }

    [Fact]
    public async Task ACreationThatNamesNoDisplayName_TakesTheUrlSegment()
    {
        Assert.Equal(201, (await Create("MKCALENDAR", "trips")).StatusCode);

        using var db = server.CreateContext();
        // A calendar with no name is a blank row in every client's sidebar.
        Assert.Equal("trips", db.Calendars.Single(c => c.DavName == "trips").DisplayName);
    }

    [Fact]
    public async Task ACreationWithNoBodyAtAll_StillCreates()
    {
        var response = await server.SendAsync("MKCALENDAR", DavPaths.Calendar(UserId, "trips"));

        Assert.Equal(201, response.StatusCode);
    }

    [Fact]
    public async Task AMkcolWithNoBody_Answers403ValidResourcetype()
    {
        var response = await server.SendAsync("MKCOL", DavPaths.Calendar(UserId, "trips"));

        // RFC 5689 § 3: the extended MKCOL says what it is creating — and it scopes itself to the
        // one carrying a body, so a bodyless MKCOL answers RFC 4918 § 9.3's bare error, never a
        // propstat naming a property nobody sent.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "error", XDocument.Parse(response.Body).Root!.Name);
        Assert.Equal(DavXml.Dav + "valid-resourcetype", ConditionOf(response));
        Assert.Empty(Stored("trips"));
    }

    [Theory]
    [InlineData("MKCALENDAR")]
    [InlineData("MKCOL")]
    public async Task AResourceTypeThatIsNotACalendar_Answers403ValidResourcetype(string method)
    {
        var response = await Create(method, "trips", new XElement(DavXml.Dav + "resourcetype",
            new XElement(DavXml.Dav + "collection"), new XElement(DavXml.CalDav + "calendar"),
            new XElement(DavXml.CalendarServer + "subscribed")));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "valid-resourcetype", ConditionOf(response));
    }

    [Theory]
    [InlineData("MKCALENDAR")]
    [InlineData("MKCOL")]
    public async Task AZoneThatResolvesToNothing_Answers403ValidCalendarData(string method)
    {
        var response = await Create(method, "trips",
            new XElement(DavXml.CalDav + "calendar-timezone",
                MkCalendarRequestTests.Zones("Mars/Olympus")));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CalDav + "valid-calendar-data", ConditionOf(response));
        Assert.Empty(Stored("trips"));
    }

    [Theory]
    [InlineData("MKCALENDAR")]
    [InlineData("MKCOL")]
    public async Task AZoneWithAVEventBesideIt_Answers403ValidCalendarDataAndCreatesNothing(string method)
    {
        // RFC 4791 § 5.2.2: « exactly one VTIMEZONE component ». A zone that resolves is not enough
        // when the object carries an event too — refused whole, never a calendar created on half of it.
        var response = await Create(method, "trips",
            new XElement(DavXml.CalDav + "calendar-timezone",
                Ics.Single("DTSTART:20260907T090000Z", null, zone: Ics.FixedZone("America/New_York", "-0500"))));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CalDav + "valid-calendar-data", ConditionOf(response));
        Assert.Empty(Stored("trips"));
    }

    [Fact]
    public async Task AMkcolRefusedOnAProperty_AnswersMkcolResponseWithThePreconditionInside()
    {
        // RFC 5689 § 3: a property failure answers a single DAV:mkcol-response holding propstat
        // elements — its § 3.5 example puts DAV:valid-resourcetype inside that propstat's error.
        var body = new XElement(DavXml.Dav + "mkcol",
            new XElement(DavXml.Dav + "set", new XElement(DavXml.Prop,
                new XElement(DavXml.Dav + "resourcetype", new XElement(DavXml.Dav + "collection")))));

        var response = await server.SendAsync("MKCOL", DavPaths.Calendar(UserId, "trips"), body.ToString());

        Assert.Equal(403, response.StatusCode);
        var document = XDocument.Parse(response.Body).Root!;
        Assert.Equal(DavXml.Dav + "mkcol-response", document.Name);
        var propstat = document.Descendants(DavXml.Dav + "propstat").Single();
        Assert.Equal(DavXml.Dav + "resourcetype",
            propstat.Element(DavXml.Prop)!.Elements().Single().Name);
        Assert.Equal("HTTP/1.1 403 Forbidden", propstat.Element(DavXml.Status)!.Value);
        Assert.Equal(DavXml.Dav + "valid-resourcetype",
            propstat.Element(DavXml.Dav + "error")!.Elements().Single().Name);
        Assert.Empty(Stored("trips"));
    }

    [Fact]
    public async Task AMkcolWithAnUnreadableTimezone_AnswersMkcolResponseToo()
    {
        var body = new XElement(DavXml.Dav + "mkcol",
            new XElement(DavXml.Dav + "set", new XElement(DavXml.Prop,
                ResourceType,
                new XElement(DavXml.CalDav + "calendar-timezone", "not an iCalendar object"))));

        var response = await server.SendAsync("MKCOL", DavPaths.Calendar(UserId, "trips"), body.ToString());

        Assert.Equal(403, response.StatusCode);
        var document = XDocument.Parse(response.Body).Root!;
        Assert.Equal(DavXml.Dav + "mkcol-response", document.Name);
        var propstat = document.Descendants(DavXml.Dav + "propstat").Single();
        Assert.Equal(DavXml.CalDav + "calendar-timezone",
            propstat.Element(DavXml.Prop)!.Elements().Single().Name);
        Assert.Equal(DavXml.CalDav + "valid-calendar-data",
            propstat.Element(DavXml.Dav + "error")!.Elements().Single().Name);
    }

    [Fact]
    public async Task AMkcalendarWithAnUnreadableTimezone_KeepsItsBareError()
    {
        // RFC 4791 § 5.3.1 names CALDAV:valid-calendar-data as a precondition, and RFC 4918 § 16
        // gives a named precondition this very shape. Only the extended MKCOL moves.
        var body = new XElement(DavXml.CalDav + "mkcalendar",
            new XElement(DavXml.Dav + "set", new XElement(DavXml.Prop,
                new XElement(DavXml.CalDav + "calendar-timezone", "not an iCalendar object"))));

        var response = await server.SendAsync("MKCALENDAR", DavPaths.Calendar(UserId, "trips"), body.ToString());

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "error", XDocument.Parse(response.Body).Root!.Name);
        Assert.Equal(DavXml.CalDav + "valid-calendar-data", ConditionOf(response));
    }

    [Theory]
    [InlineData("MKCALENDAR", "urn:ietf:params:xml:ns:caldav", "mkcalendar-response", 207)]
    [InlineData("MKCOL", "DAV:", "mkcol-response", 403)]
    public async Task AVtodoComponentSet_AnswersUnderTheVerbsOwnRootAndCreatesNothing(
        string method, string ns, string root, int status)
    {
        var response = await Create(method, "trips",
            new XElement(DavXml.CalDav + "supported-calendar-component-set",
                new XElement(DavXml.CalDav + "comp", new XAttribute("name", "VTODO"))));

        // The two verbs part company on the status line: RFC 4791 § 5.3.1.1 names 207 for
        // MKCALENDAR, RFC 5689 § 3 answers 403 for the extended MKCOL — where a 207 would be a 2xx,
        // read as "created" by a client judging on the class. DAVx5 takes this very branch.
        Assert.Equal(status, response.StatusCode);
        var document = XDocument.Parse(response.Body).Root!;
        // RFC 4791 § 5.3.1 and RFC 5689 § 3: each verb answers under its OWN root, never
        // DAV:multistatus, and neither carries an href — nothing was created to name.
        Assert.Equal(XNamespace.Get(ns) + root, document.Name);
        Assert.Empty(document.Descendants(DavXml.Href));
        Assert.Equal("HTTP/1.1 403 Forbidden",
            document.Descendants(DavXml.Status).Single().Value);
        Assert.Equal(DavXml.CalDav + "supported-calendar-component-set",
            document.Descendants(DavXml.Prop).Single().Elements().Single().Name);
        Assert.Empty(Stored("trips"));
    }

    [Theory]
    [InlineData("MKCALENDAR", 207)]
    [InlineData("MKCOL", 403)]
    public async Task AComponentSetNamingNothing_IsRefusedLikeAnUnknownOne(string method, int status)
    {
        // Zero comp names no component, so it does not name VEVENT either: All() on an empty
        // sequence said yes, and a calendar serving nothing was created.
        var response = await Create(method, "trips",
            new XElement(DavXml.CalDav + "supported-calendar-component-set"));

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(DavXml.CalDav + "supported-calendar-component-set",
            XDocument.Parse(response.Body).Descendants(DavXml.Prop).Single().Elements().Single().Name);
        Assert.Empty(Stored("trips"));
    }

    [Theory]
    [InlineData("MKCALENDAR")]
    [InlineData("MKCOL")]
    public async Task EveryCreationAnswer_CarriesNoCache(string method)
    {
        // RFC 4791 § 5.3.1 Marshalling and RFC 4918 § 9.3 put the header on the response, not on
        // the success: a refusal a proxy caches is a calendar a client cannot create twice.
        var refusedComponent = await Create(method, "trips",
            new XElement(DavXml.CalDav + "supported-calendar-component-set",
                new XElement(DavXml.CalDav + "comp", new XAttribute("name", "VTODO"))));
        var alreadyThere = await Create(method, CalendarStore.DefaultDavName, Displayname("Again"));
        var insideACalendar = await server.SendAsync(
            method, DavPaths.Calendar(UserId, CalendarStore.DefaultDavName) + "nested/");

        Assert.Equal(DavHeaders.NoCache, refusedComponent.Header("Cache-Control"));
        Assert.Equal(DavHeaders.NoCache, alreadyThere.Header("Cache-Control"));
        Assert.Equal(DavHeaders.NoCache, insideACalendar.Header("Cache-Control"));
    }

    [Fact]
    public async Task AComponentSetAskingForVeventAlone_Creates()
    {
        var response = await Create("MKCALENDAR", "trips",
            new XElement(DavXml.CalDav + "supported-calendar-component-set",
                new XElement(DavXml.CalDav + "comp", new XAttribute("name", "VEVENT"))));

        Assert.Equal(201, response.StatusCode);
    }

    [Theory]
    [InlineData("MKCALENDAR")]
    [InlineData("MKCOL")]
    public async Task AUrlAnotherCalendarAlreadyHolds_Answers405WithTheCalendarsAllow(string method)
    {
        var response = await Create(method, CalendarStore.DefaultDavName, Displayname("Again"));

        // RFC 4918 § 9.3.1 reserves 405 to a resource that is already there.
        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.CalendarAllow, response.Header("Allow"));
        Assert.Single(Stored(CalendarStore.DefaultDavName));
    }

    [Theory]
    [InlineData("MKCALENDAR")]
    [InlineData("MKCOL")]
    public async Task TheHomeItself_Answers405WithItsOwnAllow(string method)
    {
        var response = await server.SendAsync(method, DavPaths.CalendarHome(UserId));

        // The home exists, so it is § 9.3.1's 405 and not location-ok, which says "not at this
        // place in the tree" — which the home is not.
        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.CalendarHomeAllow, response.Header("Allow"));
    }

    [Theory]
    [InlineData("MKCALENDAR")]
    [InlineData("MKCOL")]
    public async Task ATargetInsideACalendar_Answers403LocationOk(string method)
    {
        var response = await server.SendAsync(
            method, DavPaths.Calendar(UserId, CalendarStore.DefaultDavName) + "nested/");

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CalDav + "calendar-collection-location-ok", ConditionOf(response));
    }

    [Theory]
    [InlineData("MKCALENDAR")]
    [InlineData("MKCOL")]
    public async Task ASegmentThisTreeWillNotHold_Answers403WithNoBody(string method)
    {
        var response = await server.SendAsync(
            method, $"{DavPaths.CalendarHome(UserId)}..%5C..%5Cetc/");

        // Decision 5 of 4c: a considered answer, never a routing 404 — and no precondition names
        // a segment that is simply impossible.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(string.Empty, response.Body);
    }

    [Fact]
    public async Task TheTwentyFirstCalendar_Answers507()
    {
        for (var i = 1; i < CalendarStore.MaxPerUser; i++)
            Assert.Equal(201, (await Create("MKCALENDAR", $"c{i}")).StatusCode);

        var response = await Create("MKCALENDAR", "one-too-many");

        Assert.Equal(507, response.StatusCode);
    }

    [Fact]
    public async Task ACreationOnAnAccountHoldingNoCalendar_StoresUtc()
    {
        await using var bare = await DavTestServer.StartAsync();

        var response = await bare.SendAsync("MKCALENDAR", DavPaths.Calendar(bare.UserId, "trips"));

        // The hand-restored base of § 6: no `default` to read a zone from, and a MKCALENDAR
        // carries no browser to ask.
        Assert.Equal(201, response.StatusCode);
        using var db = bare.CreateContext();
        Assert.Equal("UTC", db.Calendars.Single().TimeZone);
    }

    [Theory]
    [InlineData("MKCALENDAR")]
    [InlineData("MKCOL")]
    public async Task ABodyTheXmlReaderCannotJudge_Answers400(string method)
    {
        var response = await server.SendAsync(
            method, DavPaths.Calendar(UserId, "trips"), "<D:mkcol xmlns:D=\"DAV:\">");

        Assert.Equal(400, response.StatusCode);
        Assert.Empty(Stored("trips"));
    }

    [Theory]
    [InlineData("MKCALENDAR")]
    [InlineData("MKCOL")]
    public async Task TheOtherVerbsDocument_Answers400(string method)
    {
        var body = method == "MKCOL"
            ? new XElement(DavXml.CalDav + "mkcalendar")
            : new XElement(DavXml.Dav + "mkcol");

        var response = await server.SendAsync(
            method, DavPaths.Calendar(UserId, "trips"), body.ToString());

        Assert.Equal(400, response.StatusCode);
    }

    [Theory]
    [InlineData("MKCALENDAR")]
    [InlineData("MKCOL")]
    public async Task UnderTheAddressBook_TheseVerbsStayTheBooksOwn405(string method)
    {
        var response = await server.SendAsync(method, DavPaths.Collection(UserId));

        // Nothing is added to the book: RFC 4791 § 5.3.1 knows location-ok under a calendar tree
        // alone, so the catch-all of the CardDAV surface answers, unchanged.
        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.CollectionAllow, response.Header("Allow"));
    }

    [Theory]
    [InlineData("MKCALENDAR")]
    [InlineData("MKCOL")]
    public async Task WithCalDavSwitchedOff_TheCreationIs403WithNoBody(string method)
    {
        await using var off = await DavTestServer.StartAsync(calDav: false);

        var response = await off.SendAsync(method, DavPaths.Calendar(off.UserId, "trips"));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(string.Empty, response.Body);
    }

    private Task<DavTestResponse> Create(string method, string davName, params XElement[] properties)
    {
        var root = method == "MKCOL" ? DavXml.Dav + "mkcol" : DavXml.CalDav + "mkcalendar";
        // MKCOL owes a resourcetype (RFC 5689 § 3), so one is posed unless the case brings its own.
        XElement[] declared =
            method == "MKCOL" && !properties.Any(p => p.Name == DavXml.Dav + "resourcetype")
                ? [ResourceType, .. properties]
                : properties;
        var body = new XElement(root,
            new XElement(DavXml.Dav + "set", new XElement(DavXml.Prop, declared)));

        return server.SendAsync(method, DavPaths.Calendar(UserId, davName), body.ToString());
    }

    private static XElement ResourceType => new(DavXml.Dav + "resourcetype",
        new XElement(DavXml.Dav + "collection"), new XElement(DavXml.CalDav + "calendar"));

    private static XElement Displayname(string value) => new(DavXml.Dav + "displayname", value);

    /// <summary>The precondition named, wherever its shape lodges it: RFC 5689 § 3.5 puts an
    /// extended MKCOL's inside the refusing propstat, RFC 4918 § 16 leaves every other one bare
    /// under the root.</summary>
    private static XName ConditionOf(DavTestResponse response) =>
        XDocument.Parse(response.Body).Root!
            .DescendantsAndSelf(DavXml.Dav + "error").First().Elements().First().Name;

    private Task<DavTestResponse> Propfind(string path, params string[] names)
    {
        var body = new XElement(DavXml.Dav + "propfind",
            new XElement(DavXml.Prop, names.Select(name => new XElement(
                name.StartsWith("calendar-", StringComparison.Ordinal)
                    ? DavXml.CalDav + name
                    : DavXml.Dav + name))));
        return server.PropfindAsync(path, "0", body.ToString());
    }

    private List<CalendarRow> Stored(string davName)
    {
        using var db = server.CreateContext();
        return [.. db.Calendars.Where(c => c.DavName == davName)];
    }

    private void GivenTheDefaultCalendar()
    {
        using var db = server.CreateContext();
        db.Calendars.Add(new CalendarRow
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            DavName = CalendarStore.DefaultDavName,
            DisplayName = "Personal",
            Description = string.Empty,
            Color = "#3b82c4",
            Order = 0,
            TimeZone = "Europe/Brussels",
            IsVisible = true,
        });
        db.SaveChanges();
    }
}
