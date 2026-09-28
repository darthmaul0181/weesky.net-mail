using System.Xml.Linq;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Services.Dav;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;
using CalendarRow = weesky.Scotty.Microservice.Data.Preferences.Calendar;

namespace weesky.Scotty.Microservice.Tests.Controllers;

/// <summary>
/// CalDAV over the birthdays calendar (spec § 6, task 4): every write is refused with
/// <c>DAV:need-privileges</c> — answered by the controller's own check, before the writer is ever
/// reached — while its own properties (name, colour) still write through PROPPATCH.
/// </summary>
public sealed class CalDavControllerBirthdaysTests : IAsyncLifetime
{
    private DavTestServer server = null!;
    private Guid calendarId;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync();
        calendarId = GivenBirthdaysCalendar();
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task A_put_into_birthdays_is_403_need_privileges()
    {
        var response = await server.SendAsync("PUT", Href("a.ics"), CalDavPutTests.Event("u1"),
            contentType: "text/calendar");

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "need-privileges", ConditionOf(response));
        using var db = server.CreateContext();
        // No writer call: nothing of the refused body ever reaches CalendarEvents.
        Assert.Empty(db.CalendarEvents);
    }

    [Fact]
    public async Task A_delete_of_a_birthday_is_403_need_privileges()
    {
        GivenABirthdayEvent("a.ics");

        var response = await server.SendAsync("DELETE", Href("a.ics"));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "need-privileges", ConditionOf(response));
        using var db = server.CreateContext();
        Assert.Single(db.CalendarEvents);
        Assert.Empty(db.CalendarTombstones);
    }

    [Fact]
    public async Task A_delete_of_an_unknown_name_in_birthdays_is_404()
    {
        // Controller ruling R10: no resource here designates nothing, the same 404 an unknown name
        // gets anywhere else — never the 403 an existing birthday earns.
        var response = await server.SendAsync("DELETE", Href("never.ics"));

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task A_delete_of_the_birthdays_collection_is_403_need_privileges()
    {
        var response = await server.SendAsync("DELETE", DavPaths.Calendar(UserId, "birthdays"));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "need-privileges", ConditionOf(response));
        using var db = server.CreateContext();
        Assert.NotNull(db.Calendars.Find(calendarId));
    }

    [Fact]
    public async Task A_proppatch_of_the_name_and_colour_still_succeeds()
    {
        var body = new XElement(DavXml.Dav + "propertyupdate",
            new XElement(DavXml.Dav + "set", new XElement(DavXml.Prop,
                new XElement(DavXml.Dav + "displayname", "Anniversaires !"),
                new XElement(DavXml.Apple + "calendar-color", "#AABBCC"))));

        var response = await server.SendAsync(
            "PROPPATCH", DavPaths.Calendar(UserId, "birthdays"), body.ToString());

        Assert.Equal(207, response.StatusCode);
        Assert.Equal("HTTP/1.1 200 OK",
            XDocument.Parse(response.Body).Descendants(DavXml.Status).Single().Value);
        using var db = server.CreateContext();
        var row = db.Calendars.Find(calendarId)!;
        Assert.Equal("Anniversaires !", row.DisplayName);
        Assert.Equal("#aabbcc", row.Color);
    }

    private string Href(string davName) => DavPaths.Event(UserId, "birthdays", davName);

    private static XName ConditionOf(DavTestResponse response) =>
        Assert.Single(XDocument.Parse(response.Body).Root!.Elements()).Name;

    private Guid GivenBirthdaysCalendar()
    {
        using var db = server.CreateContext();
        var row = new CalendarRow
        {
            Id = Guid.NewGuid(), UserId = UserId, DavName = "birthdays", DisplayName = "Birthdays",
            Description = string.Empty, Color = "#be185d", Order = 1,
            TimeZone = "Europe/Brussels", IsVisible = true, Kind = CalendarKinds.Birthdays,
        };
        db.Calendars.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    /// <summary>Seeded directly, past the projector: the row the DELETE test tries to remove.</summary>
    private void GivenABirthdayEvent(string davName)
    {
        using var db = server.CreateContext();
        db.CalendarEvents.Add(new CalendarEvent
        {
            Id = Guid.NewGuid(), CalendarId = calendarId, UserId = UserId, Uid = "birthday-1",
            DavName = davName, IcsRaw = "x", IcsHash = "h", SyncSequence = 1, UpdatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }
}
