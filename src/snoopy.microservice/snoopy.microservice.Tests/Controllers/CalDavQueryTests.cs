using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.CalDav;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using CalendarRow = weesky.Snoopy.Microservice.Data.Preferences.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>calendar-query over the real reader and this server's own database: the shapes the
/// three clients send, the calendar-data forms, the scope on an event, the grid of refusals and
/// the truncation of 4c.</summary>
public sealed class CalDavQueryTests : IAsyncLifetime
{
    private static readonly XName CalendarData = DavXml.CalDav + "calendar-data";

    private readonly Mock<ILogger<CalDavController>> logger = new();

    /// <summary>Shared by <see cref="Row"/>, which two files now call: the ranks only ever need to
    /// grow, and one counter per process grows for every caller at once.</summary>
    private static ulong sequence;

    private DavTestServer server = null!;
    private Guid work;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync(overrides: services => services.AddSingleton(logger.Object));
        work = GivenCalendar(server, "work");
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task TheDavx5Shape_TimeRangeWithCalendarData_ServesTheEventsOfTheWindowAsStored()
    {
        var september = CalDavPutTests.Event("ua", "Septembre");
        GivenEvent("sept.ics", september);
        GivenEvent("oct.ics", Ics.Single("DTSTART:20261001T090000Z", "DTEND:20261001T100000Z"));

        var response = await Report(Calendar(), QueryBody(
            VEvent(TimeRange("20260901T000000Z", "20260930T000000Z")), withCalendarData: true));

        Assert.Equal(207, response.StatusCode);
        Assert.Equal(DavHeaders.ComplianceClasses, response.Header("DAV"));
        Assert.Equal([Href("sept.ics")], HrefsOf(response));
        var document = XDocument.Parse(response.Body);
        Assert.Equal(Lines(september), Lines(document.Descendants(CalendarData).Single().Value));
        Assert.Single(document.Descendants(DavXml.Dav + "getetag"));
    }

    [Fact]
    public async Task TheThunderbirdShape_ABareVEventCompFilter_ServesEveryEvent()
    {
        GivenEvent("a.ics", CalDavPutTests.Event("ua"));
        GivenEvent("b.ics", Ics.Single("DTSTART:20261001T090000Z", "DTEND:20261001T100000Z"));

        var response = await Report(Calendar(), QueryBody(VEvent()));

        Assert.Equal([Href("a.ics"), Href("b.ics")], HrefsOf(response));
        Assert.Empty(XDocument.Parse(response.Body).Descendants(CalendarData));
    }

    [Fact]
    public async Task TheIosShape_AVAlarmTimeRange_ServesTheEventsWhoseAlarmRings()
    {
        GivenEvent("phone.ics", Ics.FromPhone());   // -PT15M before 07:00Z
        GivenEvent("quiet.ics", CalDavPutTests.Event("ua"));

        var response = await Report(Calendar(), QueryBody(VEvent(
            new XElement(DavXml.CalDav + "comp-filter", new XAttribute("name", "VALARM"),
                TimeRange("20260907T064000Z", "20260907T065000Z")))));

        Assert.Equal([Href("phone.ics")], HrefsOf(response));
    }

    [Fact]
    public async Task ATimeRangeOnASeries_IsJudgedOnItsInstances()
    {
        GivenEvent("rule.ics", Ics.Rule("FREQ=WEEKLY;COUNT=3"));   // the 7th, 14th, 21st

        var third = await Report(Calendar(), QueryBody(VEvent(TimeRange("20260921T000000Z", "20260922T000000Z"))));
        var none = await Report(Calendar(), QueryBody(VEvent(TimeRange("20260922T000000Z", "20260923T000000Z"))));

        Assert.Equal([Href("rule.ics")], HrefsOf(third));
        Assert.Empty(HrefsOf(none));
    }

    [Fact]
    public async Task ATimeRangeWithStartAlone_ServesAnEventBeyondTheFiveYearsTheEngineWalks()
    {
        // « Renouvellement du passeport », March 2032: the event that used to vanish from the
        // phone without anything saying so.
        GivenEvent("passeport.ics", Ics.Single("DTSTART:20320315T090000Z", "DTEND:20320315T100000Z"));

        var response = await Report(Calendar(), QueryBody(VEvent(TimeRange("20260907T000000Z", null))));

        Assert.Equal(207, response.StatusCode);
        Assert.Contains(Href("passeport.ics"), HrefsOf(response));
    }

    [Fact]
    public async Task AnEndlessSeries_MatchesAnOpenWindow_WithoutBeingWalkedToTheEnd()
    {
        GivenEvent("standup.ics", Ics.Single("DTSTART:20260907T090000Z", "DTEND:20260907T093000Z",
            "RRULE:FREQ=DAILY"));

        var response = await Report(Calendar(), QueryBody(VEvent(TimeRange("20260907T000000Z", null))));

        Assert.Equal([Href("standup.ics")], HrefsOf(response));
    }

    [Fact]
    public async Task ATimeRangeWithEndAlone_StillServesARecurringResource()
    {
        // The mirror of the query above, and the one that made every recurring resource vanish:
        // walked from DateTime.MinValue the engine throws and the blanket catch reads it as a miss.
        GivenEvent("rule.ics", Ics.Rule("FREQ=WEEKLY;COUNT=3"));   // the 7th, 14th, 21st
        GivenEvent("later.ics", Ics.Single("DTSTART:20320315T090000Z", "DTEND:20320315T100000Z"));

        var response = await Report(Calendar(), QueryBody(VEvent(TimeRange(null, "20261001T000000Z"))));

        Assert.Equal([Href("rule.ics")], HrefsOf(response));
    }

    [Fact]
    public async Task AnExpandInTheQuery_ServesOneVEventPerInstance()
    {
        GivenEvent("rule.ics", Ics.Rule("FREQ=WEEKLY"));

        var response = await Report(Calendar(), QueryBody(VEvent(TimeRange("20260901T000000Z", "20261001T000000Z")),
            expand: ("20260901T000000Z", "20261001T000000Z")));

        var served = XDocument.Parse(response.Body).Descendants(CalendarData).Single().Value;
        var reloaded = IcsDocument.TryLoad(served);
        Assert.NotNull(reloaded);
        Assert.Equal(4, reloaded.Events.Count);
        Assert.DoesNotContain("RRULE", served);
    }

    [Fact]
    public async Task AnExpansionPastTheCap_Refuses403MaxInstances_ForThatMemberAlone()
    {
        GivenRawEvent("bomb.ics", Ics.DensityBomb());
        GivenEvent("a.ics", CalDavPutTests.Event("ua"));

        var response = await Report(Calendar(), QueryBody(VEvent(),
            expand: ("20260907T090000Z", "20260915T090000Z")));

        // The document is open when the expansion fails: the refusal is the member's own, and the
        // other is still served — the multiget's rule, through the same member loop.
        Assert.Equal(207, response.StatusCode);
        Assert.Equal([Href("a.ics"), Href("bomb.ics")], HrefsOf(response));
        Assert.Equal([200, 403], StatusesOf(response));
        Assert.Single(ResponsesOf(response)[1].Element(DavXml.Error)!.Elements(CalDavError.MaxInstances));
    }

    [Fact]
    public async Task AStatusEquals_IsPreselectedOnTheColumn_AndConfirmedByTheFile()
    {
        GivenEvent("yes.ics", Ics.Single("DTSTART:20260907T090000Z", null, extra: "STATUS:CONFIRMED"));
        GivenEvent("no.ics", Ics.Single("DTSTART:20260907T090000Z", null, extra: "STATUS:TENTATIVE"));

        var response = await Report(Calendar(), QueryBody(VEvent(PropFilter("STATUS", TextMatch("confirmed", "equals")))));

        Assert.Equal([Href("yes.ics")], HrefsOf(response));
    }

    [Fact]
    public async Task AnOverrideAloneMatchingAStatusFilter_IsStillReturned()
    {
        // The column holds the master's STATUS — none here — while the filter is satisfied by any
        // one component: preselected on the column, this series would vanish from a query it
        // matches, under a 207, with nothing in the logs. The CardDAV lesson of the second FN.
        GivenEvent("series.ics", Ics.RuleWithOverride("FREQ=WEEKLY", "20260914T110000", summary: "Standup")
            .Replace("SUMMARY:Standup (moved)", "STATUS:CANCELLED\r\nSUMMARY:Standup (moved)"));

        var response = await Report(Calendar(), QueryBody(VEvent(PropFilter("STATUS", TextMatch("CANCELLED", "equals")))));

        Assert.Equal([Href("series.ics")], HrefsOf(response));
    }

    [Fact]
    public async Task AQueryOnAnEvent_IsScopedToIt_AndStillHonoursTheFilter()
    {
        GivenEvent("a.ics", CalDavPutTests.Event("ua", "Alpha"));
        GivenEvent("b.ics", CalDavPutTests.Event("ub", "Beta"));

        var all = await Report(Href("a.ics"), QueryBody(VEvent()));
        var other = await Report(Href("a.ics"), QueryBody(VEvent(PropFilter("SUMMARY", TextMatch("Beta")))));

        Assert.Equal(207, all.StatusCode);
        Assert.Equal([Href("a.ics")], HrefsOf(all));
        Assert.Equal(207, other.StatusCode);
        Assert.Empty(HrefsOf(other));
    }

    [Fact]
    public async Task AQueryOnAnEventTheCalendarDoesNotHold_Is404OnTheResource()
    {
        var response = await Report(Href("gone.ics"), QueryBody(VEvent()));

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task AStoredFileThatNoLongerParses_IsLeftOutAndLogged()
    {
        GivenRawEvent("broken.ics", "BEGIN:VCALENDAR\r\nthis is not a calendar\r\n");
        GivenEvent("a.ics", CalDavPutTests.Event("ua"));

        var response = await Report(Calendar(), QueryBody(VEvent()));

        Assert.Equal([Href("a.ics")], HrefsOf(response));
        logger.VerifyWarningLoggedContaining("broken.ics");
    }

    [Fact]
    public async Task AQueryWithoutAFilter_Is403ValidFilter()
    {
        var response = await Report(Calendar(), QueryBody(null));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(CalDavError.ValidFilter, ConditionOf(response));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("20260907T100000Z", "20260907T090000Z")]
    [InlineData("2026-09-07T09:00:00Z", null)]
    public async Task ATimeRangeTheGridRefuses_Is403ValidFilter_BeforeAnyResponse(string? start, string? end)
    {
        GivenEvent("a.ics", CalDavPutTests.Event("ua"));

        var response = await Report(Calendar(), QueryBody(VEvent(TimeRange(start, end))));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(CalDavError.ValidFilter, ConditionOf(response));
    }

    [Fact]
    public async Task AFilterTheGridCannotEvaluate_Is403SupportedFilter()
    {
        var todo = new XElement(DavXml.CalDav + "filter",
            new XElement(DavXml.CalDav + "comp-filter", new XAttribute("name", "VCALENDAR"),
                new XElement(DavXml.CalDav + "comp-filter", new XAttribute("name", "VTODO"))));
        var anyOf = VEvent(PropFilter("SUMMARY", TextMatch("x")));
        anyOf.SetAttributeValue("test", "anyof");

        foreach (var filter in new[] { todo, anyOf })
        {
            var response = await Report(Calendar(), QueryBody(filter));

            Assert.Equal(403, response.StatusCode);
            Assert.Equal(CalDavError.SupportedFilter, ConditionOf(response));
        }
    }

    [Fact]
    public async Task UnicodeCasemap_Is403SupportedCollation_InTheCalendarsNamespace()
    {
        var response = await Report(Calendar(),
            QueryBody(VEvent(PropFilter("SUMMARY", TextMatch("x", collation: DavCollation.UnicodeCasemap)))));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(CalDavError.SupportedCollation, ConditionOf(response));
    }

    [Fact]
    public async Task ABodyThatIsNotXml_Is400()
    {
        var response = await Report(Calendar(), "<C:calendar-query xmlns:C=\"urn:ietf:params:xml:ns:caldav\"><D:prop");

        Assert.Equal(400, response.StatusCode);
    }

    [Theory]
    [InlineData("collection")]
    [InlineData("home")]
    [InlineData("addressbook")]
    public async Task AQueryOffTheCalendarAndTheEvent_IsARefusal(string shape)
    {
        var response = await Report(PathOf(shape), QueryBody(VEvent()));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "supported-report", ConditionOf(response));
    }

    [Fact]
    public async Task MoreThanFiveThousandMatches_AnswerTheTruncationShape()
    {
        GivenManyEvents(MultigetReport.MaxHrefs + 1);

        var response = await Report(Calendar(), QueryBody(VEvent()));

        Assert.Equal(207, response.StatusCode);
        var responses = ResponsesOf(response);
        Assert.Equal(MultigetReport.MaxHrefs + 1, responses.Count);
        var onRequestUri = responses.Single(r => r.Element(DavXml.Href)!.Value == Calendar());
        Assert.Contains("507", onRequestUri.Element(DavXml.Status)!.Value);
        Assert.Single(onRequestUri.Descendants(CalDavError.NumberOfMatchesWithinLimits));
    }

    [Fact]
    public async Task AQueryOnTheCalendar_OpensOneSnapshot_AndOnAnEventNone()
    {
        // InMemory has no isolation: keeping its refusal of BeginTransaction fatal turns « a
        // snapshot was opened » into the one observable there is (CardDavPropfindTests' device).
        await using var strict = await DavTestServer.StartAsync(keepTransactionsFatal: true);
        var calendar = GivenCalendar(strict, "work");
        GivenEvent(strict, calendar, "a.ics", CalDavPutTests.Event("ua"));

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            strict.SendAsync("REPORT", DavPaths.Calendar(strict.UserId, "work"), QueryBody(VEvent())));

        var single = await strict.SendAsync("REPORT", DavPaths.Event(strict.UserId, "work", "a.ics"), QueryBody(VEvent()));
        Assert.Equal(207, single.StatusCode);
    }

    private Task<DavTestResponse> Report(string path, string? body) => server.SendAsync("REPORT", path, body);

    private string Calendar() => DavPaths.Calendar(UserId, "work");

    private string Href(string davName) => DavPaths.Event(UserId, "work", davName);

    private string PathOf(string shape) => shape switch
    {
        "collection" => DavPaths.CalendarCollection,
        "home" => DavPaths.CalendarHome(UserId),
        _ => DavPaths.Collection(UserId),
    };

    private static Guid GivenCalendar(DavTestServer on, string davName)
    {
        using var db = on.CreateContext();
        var row = new CalendarRow
        {
            Id = Guid.NewGuid(), UserId = on.UserId, DavName = davName, DisplayName = davName,
            Description = string.Empty, Color = "#336699", Order = 1, TimeZone = Ics.Zone, IsVisible = true,
        };
        db.Calendars.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    private void GivenEvent(string davName, string ics) => GivenEvent(server, work, davName, ics);

    /// <summary>A row projected as the writer projects it, so the preselection reads real columns.</summary>
    private void GivenEvent(DavTestServer on, Guid calendarId, string davName, string ics)
    {
        using var db = on.CreateContext();
        db.CalendarEvents.Add(Row(on.UserId, calendarId, davName, ics,
            IcsProjector.Project(IcsDocument.TryLoad(ics)!, Ics.Zone)));
        db.SaveChanges();
    }

    /// <summary>Straight into the row, past the gate that would refuse it on PUT: what a base
    /// restored from before the gate may hold.</summary>
    private void GivenRawEvent(string davName, string ics)
    {
        using var db = server.CreateContext();
        var columns = IcsProjector.Project(IcsDocument.TryLoad(CalDavPutTests.Event("raw"))!, Ics.Zone);
        db.CalendarEvents.Add(Row(UserId, work, davName, ics, columns));
        db.SaveChanges();
    }

    private void GivenManyEvents(int count)
    {
        using var db = server.CreateContext();
        var ics = CalDavPutTests.Event("many");
        var columns = IcsProjector.Project(IcsDocument.TryLoad(ics)!, Ics.Zone);
        db.CalendarEvents.AddRange(Enumerable.Range(0, count).Select(i => Row(UserId, work, $"e{i:D5}.ics", ics, columns)));
        db.SaveChanges();
    }

    /// <summary>Internal so <c>AppleDiscoveryReplayTests</c> seeds through this one projection:
    /// two copies of it would drift the moment a column moves.</summary>
    internal static CalendarEvent Row(Guid owner, Guid calendarId, string davName, string ics, EventProjection columns) => new()
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
        SyncSequence = Interlocked.Increment(ref sequence),
        UpdatedAt = new DateTime(2026, 9, 7, 6, 0, 0, DateTimeKind.Utc),
    };

    private static string QueryBody(XElement? filter, bool withCalendarData = false,
        (string Start, string End)? expand = null)
    {
        var prop = new XElement(DavXml.Prop, new XElement(DavXml.Dav + "getetag"));
        if (withCalendarData || expand is not null)
        {
            var calendarData = new XElement(CalendarData);
            if (expand is { } window)
                calendarData.Add(new XElement(DavXml.CalDav + "expand",
                    new XAttribute("start", window.Start), new XAttribute("end", window.End)));
            prop.Add(calendarData);
        }

        return new XDocument(new XElement(DavXml.CalDav + "calendar-query", prop, filter)).ToString();
    }

    private static XElement VEvent(params object[] children) =>
        new(DavXml.CalDav + "filter",
            new XElement(DavXml.CalDav + "comp-filter", new XAttribute("name", "VCALENDAR"),
                new XElement(DavXml.CalDav + "comp-filter", new XAttribute("name", "VEVENT"), children)));

    private static XElement PropFilter(string name, params object[] children) =>
        new(DavXml.CalDav + "prop-filter", new XAttribute("name", name), children);

    private static XElement TextMatch(string value, string? matchType = null, string? collation = null)
    {
        var element = new XElement(DavXml.CalDav + "text-match", value);
        if (matchType is not null) element.SetAttributeValue("match-type", matchType);
        if (collation is not null) element.SetAttributeValue("collation", collation);
        return element;
    }

    private static XElement TimeRange(string? start, string? end)
    {
        var element = new XElement(DavXml.CalDav + "time-range");
        if (start is not null) element.SetAttributeValue("start", start);
        if (end is not null) element.SetAttributeValue("end", end);
        return element;
    }

    private static XName ConditionOf(DavTestResponse response) =>
        XDocument.Parse(response.Body).Root!.Elements().Single().Name;

    private static List<XElement> ResponsesOf(DavTestResponse response) =>
        [.. XDocument.Parse(response.Body).Root!.Elements(DavXml.Response)];

    private static List<string> HrefsOf(DavTestResponse response) =>
        [.. ResponsesOf(response).Select(r => r.Element(DavXml.Href)!.Value)];

    private static List<int> StatusesOf(DavTestResponse response) =>
        [.. ResponsesOf(response).Select(r => int.Parse(r.Descendants(DavXml.Status).First().Value.Split(' ')[1]))];

    private static string[] Lines(string ics) => ics.Split(["\r\n", "\n"], StringSplitOptions.None);
}
