using System.Xml.Linq;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using CalendarRow = weesky.Snoopy.Microservice.Data.Preferences.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// What is left of the calendar surface once PROPFIND and GET are served: OPTIONS, the 405 a method
/// we do not serve earns, the 308 that canonicalises a collection URL, and the switch.
/// </summary>
public sealed class CalDavSurfaceTests : IAsyncLifetime
{
    private DavTestServer server = null!;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync();
        GivenCalendar();
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task Options_OnTheCollectionOfHomes_AnnouncesTheCollectionVerbs()
    {
        var response = await server.SendAsync("OPTIONS", DavPaths.CalendarCollection);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(DavHeaders.HomeAllow, response.Header("Allow"));
        Assert.Equal(DavHeaders.ComplianceClasses, response.Header("DAV"));
    }

    [Fact]
    public async Task Options_OnTheHome_AnnouncesTheTwoCreationVerbs()
    {
        var response = await server.SendAsync("OPTIONS", DavPaths.CalendarHome(UserId));

        // The parent one creates in, which is where a client reads the capability.
        Assert.Equal(DavHeaders.CalendarHomeAllow, response.Header("Allow"));
    }

    [Fact]
    public async Task Options_OnACalendar_AnnouncesWhatThatShapeServes()
    {
        var response = await server.SendAsync("OPTIONS", DavPaths.Calendar(UserId, "work"));

        Assert.Equal(DavHeaders.CalendarAllow, response.Header("Allow"));
    }

    [Fact]
    public async Task Options_OnAnEvent_AnnouncesTheMemberVerbs()
    {
        // No event is seeded: answered off the URL shape alone, so a capabilities question can
        // never confirm that a resource exists.
        var response = await server.SendAsync("OPTIONS", DavPaths.Event(UserId, "work", "a.ics"));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(DavHeaders.EventAllow, response.Header("Allow"));
    }

    [Fact]
    public async Task Options_AnswersUnauthenticatedToo()
    {
        var response = await server.SendUnauthenticated("OPTIONS", DavPaths.Calendar(UserId, "work"));

        Assert.Equal(200, response.StatusCode);
    }

    [Theory]
    [InlineData("PROPFIND")]
    [InlineData("REPORT")]
    [InlineData("GET")]
    public async Task EverythingButOptions_StillDemandsCredentials(string method)
    {
        var response = await server.SendUnauthenticated(method, DavPaths.Calendar(UserId, "work"));

        Assert.Equal(401, response.StatusCode);
    }

    [Theory]
    [InlineData("MOVE")]
    [InlineData("COPY")]
    [InlineData("LOCK")]
    public async Task AMethodWeDoNotServe_Answers405WithAllow(string method)
    {
        var response = await server.SendAsync(method, DavPaths.Calendar(UserId, "work"));

        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.CalendarAllow, response.Header("Allow"));
    }

    [Fact]
    public async Task AGetOnACalendar_Answers405RatherThanARoutingFourOhFour()
    {
        var response = await server.SendAsync("GET", DavPaths.Calendar(UserId, "work"));

        // Generic WebDAV clients GET the collection; a routing 404 there says the collection does
        // not exist, which is the one thing it is not.
        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.CalendarAllow, response.Header("Allow"));
    }

    [Fact]
    public async Task ACollectionUrlWithoutItsSlash_Answers308()
    {
        var withoutSlash = DavPaths.Calendar(UserId, "work").TrimEnd('/');

        var response = await server.SendAsync("PROPFIND", withoutSlash, depth: "0");

        // A 308 and never a 301: a 301 lets bare OkHttp replay a REPORT as a GET, losing both the
        // method and the body.
        Assert.Equal(308, response.StatusCode);
        Assert.Equal(DavPaths.Calendar(UserId, "work"), response.Header("Location"));
    }

    [Fact]
    public async Task TheHomeWithoutItsSlash_Answers308()
    {
        var response = await server.SendAsync(
            "PROPFIND", DavPaths.CalendarHome(UserId).TrimEnd('/'), depth: "0");

        Assert.Equal(308, response.StatusCode);
        Assert.Equal(DavPaths.CalendarHome(UserId), response.Header("Location"));
    }

    [Fact]
    public async Task AReportTheShapeDoesNotServe_Is403SupportedReport()
    {
        var body = new XElement(DavXml.CalDav + "calendar-query",
            new XElement(DavXml.Prop, new XElement(DavXml.Dav + "getetag"))).ToString();

        // A query on the home: RFC 4791 § 7.8 defines it on a calendar and on an event alone.
        var response = await server.SendAsync("REPORT", DavPaths.CalendarHome(UserId), body);

        // The considered answer: a report asked off the shape that serves it is a 403, and a 500
        // would make a client retry for ever.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "supported-report",
            XDocument.Parse(response.Body).Root!.Elements().First().Name);
    }

    [Fact]
    public async Task CalDavSwitchedOff_Is403OnTheWholeCalendarTree()
    {
        await using var off = await DavTestServer.StartAsync(calDav: false);

        foreach (var path in new[]
                 {
                     DavPaths.CalendarCollection, DavPaths.CalendarHome(off.UserId),
                     DavPaths.Calendar(off.UserId, "work"),
                 })
        {
            var response = await off.PropfindAsync(path, "0", null);

            Assert.Equal(403, response.StatusCode);
            Assert.Equal(string.Empty, response.Body);
        }
    }

    [Fact]
    public async Task CalDavSwitchedOff_LeavesTheAddressBookAlone()
    {
        await using var off = await DavTestServer.StartAsync(calDav: false);

        var response = await off.PropfindAsync(DavPaths.Collection(off.UserId), "0", null);

        Assert.Equal(207, response.StatusCode);
    }

    private void GivenCalendar()
    {
        using var db = server.CreateContext();
        db.Calendars.Add(new CalendarRow
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            DavName = "work",
            DisplayName = "Work",
            Description = string.Empty,
            Color = "#336699",
            Order = 1,
            TimeZone = "Europe/Brussels",
            IsVisible = true,
        });
        db.SaveChanges();
    }
}
