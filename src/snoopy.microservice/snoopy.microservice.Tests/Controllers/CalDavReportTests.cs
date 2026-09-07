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

/// <summary>calendar-multiget and expand-property on the calendar tree, over the real reader and
/// this server's own database. calendar-query and free-busy-query are T7 and T8.</summary>
public sealed class CalDavReportTests : IAsyncLifetime
{
    private static readonly XName CalendarData = DavXml.CalDav + "calendar-data";

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
    public async Task AMultiget_AnswersEachHrefInTheBodysOrder_AndOnlyThisCalendarsOwn()
    {
        GivenEvent(work, "a.ics", CalDavPutTests.Event("ua"));
        GivenEvent(work, "b.ics", CalDavPutTests.Event("ub"));
        // The SAME name in the other calendar and under the other user: a member lookup that read
        // the name alone would find this calendar's a.ics behind both hrefs and answer 200.
        GivenEvent(other, "a.ics", CalDavPutTests.Event("uc"));
        var foreign = Guid.NewGuid();
        GivenEvent(GivenCalendar(foreign, "work"), "a.ics", CalDavPutTests.Event("ux"));

        string[] hrefs =
        [
            Href("b.ics"), Href("a.ics"), DavPaths.Event(foreign, "work", "a.ics"),
            DavPaths.Event(UserId, "other", "a.ics"), DavPaths.Card(UserId, "a.vcf"),
        ];
        var response = await Report(DavPaths.Calendar(UserId, "work"), MultigetBody(hrefs));

        // Both foreign resources EXIST under their very href: anything but a bare 404 leaks them,
        // and a global error would throw away the two that were found.
        Assert.Equal(207, response.StatusCode);
        Assert.Equal(DavHeaders.ComplianceClasses, response.Header("DAV"));
        Assert.Equal(hrefs, HrefsOf(response));
        Assert.Equal([200, 200, 404, 404, 404], StatusesOf(response));
    }

    [Fact]
    public async Task AMultiget_ServesTheFileAsStoredInCalendarData()
    {
        var ics = CalDavPutTests.Event("ua", "Adèle");
        GivenEvent(work, "a.ics", ics, hash: "9f1c2d");

        var response = await Report(DavPaths.Calendar(UserId, "work"),
            MultigetBody([Href("a.ics")], withCalendarData: true));

        // An XML reader folds CRLF to LF in text (XML 1.0 § 2.11): the lines, not the bytes, are
        // what a multistatus can carry — the bytes are the GET's.
        var document = XDocument.Parse(response.Body);
        Assert.Equal(Lines(ics), Lines(document.Descendants(CalendarData).Single().Value));
        Assert.Equal("\"9f1c2d\"", document.Descendants(DavXml.Dav + "getetag").Single().Value);
    }

    [Fact]
    public async Task AMultigetWithExpand_ServesOneVeventPerInstance()
    {
        GivenEvent(work, "rule.ics", Ics.Rule("FREQ=WEEKLY"));

        var response = await Report(DavPaths.Calendar(UserId, "work"),
            MultigetBody([Href("rule.ics")], expand: ("20260901T000000Z", "20261001T000000Z")));

        Assert.Equal(207, response.StatusCode);
        var served = XDocument.Parse(response.Body).Descendants(CalendarData).Single().Value;
        var reloaded = IcsDocument.TryLoad(served);
        Assert.NotNull(reloaded);
        Assert.Equal(4, reloaded.Events.Count);
        Assert.DoesNotContain("RRULE", served);
        Assert.Contains("RECURRENCE-ID:20260914T070000Z", served);
    }

    [Fact]
    public async Task AnExpandMissingItsEnd_Is403ValidFilter_BeforeAnyResponse()
    {
        GivenEvent(work, "a.ics", CalDavPutTests.Event("ua"));

        var response = await Report(DavPaths.Calendar(UserId, "work"),
            MultigetBody([Href("a.ics")], expand: ("20260901T000000Z", null)));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(CalDavError.ValidFilter, ConditionOf(response));
    }

    [Fact]
    public async Task AnExpansionPastTheCap_Refuses403MaxInstances_ForThatMemberAlone()
    {
        // Straight into the rows, past the gate that would refuse it on PUT: what a base restored
        // from before the gate may hold.
        GivenEvent(work, "bomb.ics", Ics.DensityBomb());
        GivenEvent(work, "a.ics", CalDavPutTests.Event("ua"));

        var response = await Report(DavPaths.Calendar(UserId, "work"),
            MultigetBody([Href("bomb.ics"), Href("a.ics")],
                expand: ("20260907T090000Z", "20260915T090000Z")));

        // The document is already open when the expansion fails, so the refusal is the member's
        // own — named, never a truncated 207 that says nothing — and the other is still served.
        Assert.Equal(207, response.StatusCode);
        var responses = ResponsesOf(response);
        Assert.Equal("HTTP/1.1 403 Forbidden", responses[0].Element(DavXml.Status)!.Value);
        Assert.Single(responses[0].Element(DavXml.Error)!.Elements(CalDavError.MaxInstances));
        Assert.Equal([403, 200], StatusesOf(response));
        Assert.Single(responses[1].Descendants(CalendarData));
    }

    [Fact]
    public async Task MoreThanFiveThousandHrefs_AnswersTheTruncationShape()
    {
        var body = MultigetBody([.. Enumerable.Range(0, MultigetReport.MaxHrefs + 1)
            .Select(i => Href($"e{i}.ics"))]);

        var response = await Report(DavPaths.Calendar(UserId, "work"), body);

        Assert.Equal(207, response.StatusCode);
        var onRequestUri = ResponsesOf(response).Single(r =>
            r.Element(DavXml.Href)!.Value == DavPaths.Calendar(UserId, "work"));
        Assert.Contains("507", onRequestUri.Element(DavXml.Status)!.Value);
        Assert.Single(onRequestUri.Descendants(CalDavError.NumberOfMatchesWithinLimits));
    }

    [Fact]
    public async Task AMultigetOnAnEvent_IsScopedToThatEvent()
    {
        GivenEvent(work, "a.ics", CalDavPutTests.Event("ua"));
        GivenEvent(work, "b.ics", CalDavPutTests.Event("ub"));

        var response = await Report(Href("a.ics"), MultigetBody([Href("a.ics"), Href("b.ics")]));

        // RFC 4791 § 7.9 defines the report on a calendar object resource too, and there the
        // hrefs may only name it: a sibling is not a member of the resource addressed.
        Assert.Equal(207, response.StatusCode);
        Assert.Equal([Href("a.ics"), Href("b.ics")], HrefsOf(response));
        Assert.Equal([200, 404], StatusesOf(response));
    }

    [Fact]
    public async Task AnUnknownEventAsTheRequestUri_Is404OnTheResource()
    {
        var response = await Report(Href("gone.ics"), MultigetBody([Href("gone.ics")]));

        Assert.Equal(404, response.StatusCode);
    }

    [Theory]
    [InlineData("collection")]
    [InlineData("home")]
    [InlineData("principal")]
    public async Task AMultigetOffTheCalendarAndTheEvent_IsARefusal(string shape)
    {
        var response = await Report(PathOf(shape), MultigetBody([Href("a.ics")]));

        // supported-report-set announces the report on the calendar and the event alone.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "supported-report", ConditionOf(response));
    }

    [Fact]
    public async Task AnAddressbookMultigetOnACalendar_IsARefusalToo()
    {
        var body = new XDocument(new XElement(DavXml.CardDav + "addressbook-multiget",
            new XElement(DavXml.Prop, new XElement(DavXml.Dav + "getetag")),
            new XElement(DavXml.Href, Href("a.ics")))).ToString();

        var response = await Report(DavPaths.Calendar(UserId, "work"), body);

        // The two multigets share a shape and not a name: served here, an address book's
        // report would read event hrefs as cards.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "supported-report", ConditionOf(response));
    }

    [Fact]
    public async Task ExpandProperty_OnTheCalendar_ResolvesTheOwner()
    {
        var response = await Report(DavPaths.Calendar(UserId, "work"), ExpandPropertyBody(
            DavXml.Dav + "owner", DavXml.Dav + "displayname"));

        Assert.Equal(207, response.StatusCode);
        var nested = XDocument.Parse(response.Body)
            .Descendants(DavXml.Dav + "owner").Descendants(DavXml.Response).Single();
        Assert.Equal(DavPaths.Principal(UserId), nested.Element(DavXml.Href)!.Value);
        // Resolved on the principal, not echoed: its displayname is the address.
        Assert.Equal(server.Email, nested.Descendants(DavXml.Dav + "displayname").Single().Value);
    }

    [Fact]
    public async Task ExpandProperty_OnTheHome_ResolvesCurrentUserPrincipal()
    {
        var response = await Report(DavPaths.CalendarHome(UserId), ExpandPropertyBody(
            DavXml.Dav + "current-user-principal", DavXml.CalDav + "calendar-home-set"));

        Assert.Equal(207, response.StatusCode);
        var nested = XDocument.Parse(response.Body)
            .Descendants(DavXml.Dav + "current-user-principal").Descendants(DavXml.Response).Single();
        Assert.Equal(DavPaths.Principal(UserId), nested.Element(DavXml.Href)!.Value);
        Assert.Equal(DavPaths.CalendarHome(UserId),
            nested.Descendants(DavXml.CalDav + "calendar-home-set").Single().Element(DavXml.Href)!.Value);
    }

    [Fact]
    public async Task AnExpandPropertyOnAnEvent_IsARefusal()
    {
        GivenEvent(work, "a.ics", CalDavPutTests.Event("ua"));

        var response = await Report(Href("a.ics"), ExpandPropertyBody(
            DavXml.Dav + "owner", DavXml.Dav + "displayname"));

        // No property of an event is href-valued: nothing to expand, and none announced.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "supported-report", ConditionOf(response));
    }

    [Fact]
    public async Task AnotherUsersCalendar_Answers404BeforeAnythingElse()
    {
        var stranger = Guid.NewGuid();

        var response = await Report(DavPaths.Calendar(stranger, "work"),
            MultigetBody([DavPaths.Event(stranger, "work", "a.ics")]));

        Assert.Equal(404, response.StatusCode);
    }

    private Task<DavTestResponse> Report(string path, string? body) =>
        server.SendAsync("REPORT", path, body);

    private string Href(string davName) => DavPaths.Event(UserId, "work", davName);

    private string PathOf(string shape) => shape switch
    {
        "collection" => DavPaths.CalendarCollection,
        "home" => DavPaths.CalendarHome(UserId),
        _ => DavPaths.Principal(UserId),
    };

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

    private void GivenEvent(Guid calendarId, string davName, string ics, string? hash = null)
    {
        using var db = server.CreateContext();
        var owner = db.Calendars.Single(c => c.Id == calendarId).UserId;
        db.CalendarEvents.Add(new CalendarEvent
        {
            Id = Guid.NewGuid(),
            CalendarId = calendarId,
            UserId = owner,
            Uid = Guid.NewGuid().ToString(),
            DavName = davName,
            StartsAt = new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
            EndsAt = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc),
            FirstOccurrence = new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
            LastOccurrence = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc),
            Transparency = "OPAQUE",
            IcsRaw = ics,
            IcsHash = hash ?? $"hash-of-{davName}",
            SyncSequence = ++sequence,
            UpdatedAt = new DateTime(2026, 9, 7, 6, 0, 0, DateTimeKind.Utc),
        });
        db.SaveChanges();
    }

    private static string MultigetBody(string[] hrefs, bool withCalendarData = false,
        (string Start, string? End)? expand = null)
    {
        var prop = new XElement(DavXml.Prop, new XElement(DavXml.Dav + "getetag"));
        if (withCalendarData || expand is not null)
        {
            var calendarData = new XElement(CalendarData);
            if (expand is { } window)
            {
                var element = new XElement(DavXml.CalDav + "expand", new XAttribute("start", window.Start));
                if (window.End is not null) element.Add(new XAttribute("end", window.End));
                calendarData.Add(element);
            }

            prop.Add(calendarData);
        }

        return new XDocument(new XElement(DavXml.CalDav + "calendar-multiget", prop,
            hrefs.Select(href => new XElement(DavXml.Href, href)))).ToString();
    }

    private static string ExpandPropertyBody(XName outer, XName inner) =>
        new XDocument(new XElement(DavXml.Dav + "expand-property",
            new XElement(DavXml.Dav + "property",
                new XAttribute("name", outer.LocalName),
                new XAttribute("namespace", outer.NamespaceName),
                new XElement(DavXml.Dav + "property",
                    new XAttribute("name", inner.LocalName),
                    new XAttribute("namespace", inner.NamespaceName))))).ToString();

    private static XName ConditionOf(DavTestResponse response) =>
        XDocument.Parse(response.Body).Root!.Elements().Single().Name;

    private static List<XElement> ResponsesOf(DavTestResponse response) =>
        [.. XDocument.Parse(response.Body).Root!.Elements(DavXml.Response)];

    private static List<string> HrefsOf(DavTestResponse response) =>
        [.. ResponsesOf(response).Select(r => r.Element(DavXml.Href)!.Value)];

    private static string[] Lines(string ics) => ics.Split(["\r\n", "\n"], StringSplitOptions.None);

    /// <summary>The first status of each response — the one Thunderbird reads.</summary>
    private static List<int> StatusesOf(DavTestResponse response) =>
        [.. ResponsesOf(response).Select(r =>
            int.Parse(r.Descendants(DavXml.Status).First().Value.Split(' ')[1]))];
}
