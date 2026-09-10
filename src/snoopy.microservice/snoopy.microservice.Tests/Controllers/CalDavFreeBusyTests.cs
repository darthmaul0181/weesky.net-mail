using System.Xml.Linq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services.CalDav;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using CalendarRow = weesky.Snoopy.Microservice.Data.Preferences.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary><c>CALDAV:free-busy-query</c> (RFC 4791 § 7.10), over the real reader and this
/// server's own database.</summary>
public sealed class CalDavFreeBusyTests : IAsyncLifetime
{
    private DavTestServer server = null!;
    private Guid work;
    private Guid other;
    private ulong sequence;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync();
        work = GivenCalendar(UserId, "work");
        other = GivenCalendar(UserId, "other");
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task ABusyAndATentativeEvent_Answer200TextCalendarWithBothEntries()
    {
        GivenEvent(work, "a.ics",
            Ics.Single("DTSTART:20260907T090000Z", "DTEND:20260907T100000Z"));
        GivenEvent(work, "b.ics",
            Ics.Single("DTSTART:20260907T110000Z", "DTEND:20260907T120000Z", extra: "STATUS:TENTATIVE"));
        // Outside the window: must not appear.
        GivenEvent(work, "c.ics",
            Ics.Single("DTSTART:20260908T090000Z", "DTEND:20260908T100000Z"));
        // Another calendar of the same user: free-busy-query is scoped to the collection it is
        // addressed to alone.
        GivenEvent(other, "d.ics",
            Ics.Single("DTSTART:20260907T130000Z", "DTEND:20260907T140000Z"));

        var response = await Query(DavPaths.Calendar(UserId, "work"),
            "20260907T000000Z", "20260908T000000Z");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(DavHeaders.FreeBusyContentType, response.Header("Content-Type"));

        var calendar = IcsDocument.TryLoad(response.Body);
        Assert.NotNull(calendar);
        var freeBusy = Assert.Single(calendar.FreeBusy);
        // Ical.Net 5.2.3's FreeBusyEntry proxy throws on Period/Status access once reloaded from
        // text (a library quirk, not this report's) — Count is safe, the instants and FBTYPE are
        // asserted on the text itself below, the way every other report here checks calendar-data.
        Assert.Equal(2, freeBusy.Entries.Count);
        // The default clock DavTestServer poses — never a real DateTime.UtcNow.
        Assert.Equal(new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc), freeBusy.DtStamp!.AsUtc);
        Assert.Equal(new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc), freeBusy.DtStart!.AsUtc);
        Assert.Equal(new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc), freeBusy.DtEnd!.AsUtc);
        Assert.Contains("FREEBUSY:20260907T090000Z/20260907T100000Z", response.Body);
        Assert.Contains(
            "FREEBUSY;FBTYPE=BUSY-TENTATIVE:20260907T110000Z/20260907T120000Z", response.Body);
        // c.ics (outside the window) and d.ics (another calendar) must not appear as periods.
        Assert.DoesNotContain("FREEBUSY:20260908T", response.Body);
        Assert.DoesNotContain("FREEBUSY:20260907T130000", response.Body);
    }

    [Fact]
    public async Task ATransparentEventAndACancelledOne_AreBothLeftOut()
    {
        GivenEvent(work, "free.ics",
            Ics.Single("DTSTART:20260907T090000Z", "DTEND:20260907T100000Z", extra: "TRANSP:TRANSPARENT"));
        GivenEvent(work, "cancelled.ics",
            Ics.Single("DTSTART:20260907T110000Z", "DTEND:20260907T120000Z", extra: "STATUS:CANCELLED"));

        var response = await Query(DavPaths.Calendar(UserId, "work"),
            "20260907T000000Z", "20260908T000000Z");

        Assert.Equal(200, response.StatusCode);
        // On the text, not on Entries: Ical.Net 5.2.3 enumerates that collection as empty even when
        // its Count is right, so an Assert.Empty there passes whatever the server wrote.
        Assert.Contains("BEGIN:VFREEBUSY", response.Body);
        Assert.DoesNotContain(Lines(response.Body), line => line.StartsWith("FREEBUSY"));
    }

    [Fact]
    public async Task MissingEnd_Is400BeforeAnyBody()
    {
        var response = await Query(DavPaths.Calendar(UserId, "work"), "20260907T000000Z", null);

        Assert.Equal(400, response.StatusCode);
        Assert.Equal(string.Empty, response.Body);
    }

    [Fact]
    public async Task EndNotAfterStart_Is400()
    {
        var response = await Query(DavPaths.Calendar(UserId, "work"),
            "20260908T000000Z", "20260907T000000Z");

        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task NoTimeRangeAtAll_Is400()
    {
        var body = new XDocument(
            new XElement(DavXml.CalDav + "free-busy-query")).ToString();

        var response = await server.SendAsync("REPORT", DavPaths.Calendar(UserId, "work"), body);

        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task OnAnEvent_Is403SupportedReport()
    {
        GivenEvent(work, "a.ics", CalDavPutTests.Event("ua"));

        var response = await Query(DavPaths.Event(UserId, "work", "a.ics"),
            "20260907T000000Z", "20260908T000000Z");

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "supported-report",
            XDocument.Parse(response.Body).Root!.Elements().Single().Name);
    }

    [Fact]
    public async Task TheCalendarAnnouncesFreeBusyQuery_InSupportedReportSet()
    {
        var response = await server.PropfindAsync(DavPaths.Calendar(UserId, "work"), "0",
            new XElement(DavXml.Dav + "propfind",
                new XElement(DavXml.Prop, new XElement(DavXml.Dav + "supported-report-set"))).ToString());

        var names = XDocument.Parse(response.Body)
            .Descendants(DavXml.Dav + "supported-report-set")
            .Descendants(DavXml.Dav + "report")
            .Select(report => report.Elements().Single().Name);

        Assert.Contains(DavXml.CalDav + "free-busy-query", names);
    }

    private Task<DavTestResponse> Query(string path, string? start, string? end)
    {
        var timeRange = new XElement(DavXml.CalDav + "time-range");
        if (start is not null) timeRange.Add(new XAttribute("start", start));
        if (end is not null) timeRange.Add(new XAttribute("end", end));
        var body = new XDocument(
            new XElement(DavXml.CalDav + "free-busy-query", timeRange)).ToString();
        return server.SendAsync("REPORT", path, body);
    }

    private Guid GivenCalendar(Guid owner, string davName)
    {
        using var db = server.CreateContext();
        var row = new CalendarRow
        {
            Id = Guid.NewGuid(), UserId = owner, DavName = davName, DisplayName = davName,
            Description = string.Empty, Color = "#336699", Order = 1,
            TimeZone = Ics.Zone, IsVisible = true,
        };
        db.Calendars.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    /// <summary>A row projected as the writer projects it, so the candidate window reads real
    /// columns (`FirstOccurrence`/`LastOccurrence`) rather than ones the test invented.</summary>
    /// <summary>The lines of an iCalendar document. A bare Contains would match FREEBUSY inside
    /// BEGIN:VFREEBUSY, which every answer carries.</summary>
    private static string[] Lines(string ics) =>
        [.. ics.Split('\n').Select(line => line.TrimEnd('\r'))];

    private void GivenEvent(Guid calendarId, string davName, string ics)
    {
        using var db = server.CreateContext();
        var owner = db.Calendars.Single(c => c.Id == calendarId).UserId;
        var columns = IcsProjector.Project(IcsDocument.TryLoad(ics)!, Ics.Zone);
        db.CalendarEvents.Add(new CalendarEvent
        {
            Id = Guid.NewGuid(),
            CalendarId = calendarId,
            UserId = owner,
            Uid = Guid.NewGuid().ToString(),
            DavName = davName,
            StartsAt = columns.StartsAt,
            EndsAt = columns.EndsAt,
            IsAllDay = columns.IsAllDay,
            TimeZone = columns.TimeZone,
            IsRecurring = columns.IsRecurring,
            FirstOccurrence = columns.FirstOccurrence,
            LastOccurrence = columns.LastOccurrence,
            Status = columns.Status,
            Transparency = columns.Transparency,
            Class = columns.Class,
            IcsRaw = ics,
            IcsHash = IcsDocument.HashOf(ics),
            SyncSequence = ++sequence,
            UpdatedAt = new DateTime(2026, 9, 7, 6, 0, 0, DateTimeKind.Utc),
        });
        db.SaveChanges();
    }
}
