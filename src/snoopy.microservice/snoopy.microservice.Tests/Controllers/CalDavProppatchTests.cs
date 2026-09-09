using System.Xml.Linq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Services.CalDav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using CalendarRow = weesky.Snoopy.Microservice.Data.Preferences.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// PROPPATCH on the calendar tree (§ 11): the collection is the one shape whose properties a
/// client really writes, and every other shape keeps 4c decision 16's blanket 403.
/// </summary>
public sealed class CalDavProppatchTests : IAsyncLifetime
{
    private static readonly Guid Epoch = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private DavTestServer server = null!;
    private Guid calendarId;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync();
        calendarId = GivenCalendar();
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task TheFiveWritableOnes_Answer200AndTheRest403_InThatOrder()
    {
        var response = await Proppatch(DavPaths.Calendar(UserId, "work"), Set(
            new XElement(DavXml.Dav + "displayname", "Boulot"),
            new XElement(DavXml.CalDav + "calendar-description", "Réunions"),
            new XElement(DavXml.Apple + "calendar-color", "#AABBCC"),
            new XElement(DavXml.Apple + "calendar-order", "4"),
            new XElement(DavXml.CalDav + "calendar-timezone",
                MkCalendarRequestTests.Zones("Pacific/Auckland")),
            new XElement(DavXml.CalDav + "default-alarm-vevent-date", "BEGIN:VALARM")));

        Assert.Equal(207, response.StatusCode);
        var propstats = XDocument.Parse(response.Body).Descendants(DavXml.PropStat).ToList();
        // The 200 propstat FIRST: Thunderbird reads the first descendant status of a response and
        // compares it to the literal "HTTP/1.1 200 OK".
        Assert.Equal(["HTTP/1.1 200 OK", "HTTP/1.1 403 Forbidden"],
            propstats.Select(p => p.Element(DavXml.Status)!.Value));
        Assert.Equal(5, propstats[0].Element(DavXml.Prop)!.Elements().Count());
        Assert.Equal(DavXml.CalDav + "default-alarm-vevent-date",
            propstats[1].Element(DavXml.Prop)!.Elements().Single().Name);
        Assert.Equal(DavPaths.Calendar(UserId, "work"),
            XDocument.Parse(response.Body).Descendants(DavXml.Href).Single().Value);
    }

    [Fact]
    public async Task WhatWasAccepted_IsWhatThePropfindThatFollowsReads()
    {
        await Proppatch(DavPaths.Calendar(UserId, "work"), Set(
            new XElement(DavXml.Dav + "displayname", "Boulot"),
            new XElement(DavXml.Apple + "calendar-color", "#AABBCC80"),
            new XElement(DavXml.Apple + "calendar-order", "4"),
            new XElement(DavXml.CalDav + "calendar-timezone",
                MkCalendarRequestTests.Zones("Pacific/Auckland"))));

        using var db = server.CreateContext();
        var row = db.Calendars.Find(calendarId)!;
        Assert.Equal("Boulot", row.DisplayName);
        Assert.Equal("#aabbcc", row.Color);
        Assert.Equal(4, row.Order);
        Assert.Equal("Pacific/Auckland", row.TimeZone);
    }

    [Fact]
    public async Task APropertyTheBodyNeverNamed_KeepsItsValue()
    {
        await Proppatch(DavPaths.Calendar(UserId, "work"),
            Set(new XElement(DavXml.Apple + "calendar-order", "4")));

        using var db = server.CreateContext();
        // UpdateAsync overwrites the display name unconditionally: a body naming only the rank
        // would blank it if the write were not built from the calendar as it stands.
        Assert.Equal("Work", db.Calendars.Find(calendarId)!.DisplayName);
        Assert.Equal("The weekly grind", db.Calendars.Find(calendarId)!.Description);
    }

    [Fact]
    public async Task OneBadValue_Is403OnItsOwnAndTheOthersAreStored()
    {
        var response = await Proppatch(DavPaths.Calendar(UserId, "work"), Set(
            new XElement(DavXml.Dav + "displayname", "Boulot"),
            new XElement(DavXml.Apple + "calendar-color", "rebeccapurple")));

        var propstats = XDocument.Parse(response.Body).Descendants(DavXml.PropStat).ToList();
        Assert.Equal(DavXml.Dav + "displayname",
            propstats[0].Element(DavXml.Prop)!.Elements().Single().Name);
        Assert.Equal(DavXml.Apple + "calendar-color",
            propstats[1].Element(DavXml.Prop)!.Elements().Single().Name);
        using var db = server.CreateContext();
        Assert.Equal("Boulot", db.Calendars.Find(calendarId)!.DisplayName);
        Assert.Equal("#336699", db.Calendars.Find(calendarId)!.Color);
    }

    [Fact]
    public async Task RemovingTheDescription_EmptiesItAndAnswers200()
    {
        var response = await Proppatch(DavPaths.Calendar(UserId, "work"),
            Remove(new XElement(DavXml.CalDav + "calendar-description")));

        Assert.Equal("HTTP/1.1 200 OK",
            XDocument.Parse(response.Body).Descendants(DavXml.Status).Single().Value);
        using var db = server.CreateContext();
        Assert.Equal(string.Empty, db.Calendars.Find(calendarId)!.Description);
    }

    [Fact]
    public async Task RemovingTheDisplayName_Is403AndChangesNothing()
    {
        var response = await Proppatch(DavPaths.Calendar(UserId, "work"),
            Remove(new XElement(DavXml.Dav + "displayname")));

        Assert.Equal("HTTP/1.1 403 Forbidden",
            XDocument.Parse(response.Body).Descendants(DavXml.Status).Single().Value);
        using var db = server.CreateContext();
        Assert.Equal("Work", db.Calendars.Find(calendarId)!.DisplayName);
    }

    [Fact]
    public async Task NothingHereAdvancesTheCtagOrTheToken()
    {
        var before = await CtagAndToken();

        Assert.Equal(207, (await Proppatch(DavPaths.Calendar(UserId, "work"),
            Set(new XElement(DavXml.Dav + "displayname", "Boulot")))).StatusCode);

        // None of these five is a resource: advancing the counter would make every phone resync a
        // collection nothing in it changed (cadrage).
        Assert.Equal(before, await CtagAndToken());
    }

    [Fact]
    public async Task AnUnknownCalendar_Answers404RatherThanA207()
    {
        var response = await Proppatch(DavPaths.Calendar(UserId, "nope"),
            Set(new XElement(DavXml.Dav + "displayname", "Boulot")));

        // A 207 on a segment that designates nothing would tell the client the collection exists.
        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task ABodyThatIsNoPropertyupdate_Answers400()
    {
        var response = await Proppatch(DavPaths.Calendar(UserId, "work"),
            new XElement(DavXml.Dav + "propfind", new XElement(DavXml.Prop)));

        Assert.Equal(400, response.StatusCode);
    }

    [Theory]
    [InlineData("collection")]
    [InlineData("home")]
    [InlineData("event")]
    public async Task EveryOtherShape_StillRefusesEveryProperty(string shape)
    {
        await GivenAnEvent();
        var path = shape switch
        {
            "collection" => DavPaths.CalendarCollection,
            "home" => DavPaths.CalendarHome(UserId),
            _ => DavPaths.Event(UserId, "work", "a.ics"),
        };

        var response = await Proppatch(path, Set(
            new XElement(DavXml.Dav + "displayname", "Boulot"),
            new XElement(DavXml.CalDav + "calendar-description", "x")));

        Assert.Equal(207, response.StatusCode);
        var propstats = XDocument.Parse(response.Body).Descendants(DavXml.PropStat).ToList();
        Assert.Equal("HTTP/1.1 403 Forbidden",
            Assert.Single(propstats).Element(DavXml.Status)!.Value);
    }

    [Fact]
    public async Task ACalendarTimezoneCarryingAVEvent_IsRefusedAndNothingOfItIsStored()
    {
        // errors.xml/Invalid CalDAV:timezone t2: the property is « exactly one VTIMEZONE » (RFC 4791
        // § 5.2.2), and a body with an event beside it is refused whole, never half-stored.
        var response = await Proppatch(DavPaths.Calendar(UserId, "work"), Set(
            new XElement(DavXml.CalDav + "calendar-timezone",
                Ics.Single("DTSTART:20260907T090000Z", null, zone: Ics.FixedZone("America/New_York", "-0500")))));

        Assert.Equal(207, response.StatusCode);
        Assert.Equal("HTTP/1.1 403 Forbidden",
            XDocument.Parse(response.Body).Descendants(DavXml.Status).Single().Value);
        using var db = server.CreateContext();
        Assert.Equal("Europe/Brussels", db.Calendars.Find(calendarId)!.TimeZone);
    }

    private Task<DavTestResponse> Proppatch(string path, XElement body) =>
        server.SendAsync("PROPPATCH", path, body.ToString());

    private static XElement Set(params XElement[] properties) =>
        Update(DavXml.Dav + "set", properties);

    private static XElement Remove(params XElement[] properties) =>
        Update(DavXml.Dav + "remove", properties);

    private static XElement Update(XName instruction, XElement[] properties) =>
        new(DavXml.Dav + "propertyupdate",
            new XElement(instruction, new XElement(DavXml.Prop, properties)));

    private async Task<string> CtagAndToken()
    {
        var body = new XElement(DavXml.Dav + "propfind",
            new XElement(DavXml.Prop,
                new XElement(DavXml.CalendarServer + "getctag"),
                new XElement(DavXml.Dav + "sync-token"))).ToString();
        var response = await server.PropfindAsync(DavPaths.Calendar(UserId, "work"), "0", body);
        return string.Concat(
            XDocument.Parse(response.Body).Descendants(DavXml.Prop).First().Elements()
                .Select(e => e.Value));
    }

    private async Task GivenAnEvent()
    {
        using var db = server.CreateContext();
        db.CalendarEvents.Add(new CalendarEvent
        {
            Id = Guid.NewGuid(),
            CalendarId = calendarId,
            UserId = UserId,
            Uid = "u1",
            DavName = "a.ics",
            StartsAt = new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
            EndsAt = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc),
            FirstOccurrence = new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
            LastOccurrence = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc),
            IcsRaw = "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n",
            IcsHash = "hash",
            SyncSequence = 1,
            UpdatedAt = new DateTime(2026, 9, 7, 6, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();
    }

    private Guid GivenCalendar()
    {
        using var db = server.CreateContext();
        var row = new CalendarRow
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            DavName = "work",
            DisplayName = "Work",
            Description = "The weekly grind",
            Color = "#336699",
            Order = 1,
            TimeZone = "Europe/Brussels",
            IsVisible = true,
        };
        db.Calendars.Add(row);
        db.CalendarSyncStates.Add(new CalendarSyncState
        {
            CalendarId = row.Id, Epoch = Epoch, Seq = 12, PrunedBelow = 0,
        });
        db.SaveChanges();
        return row.Id;
    }
}
