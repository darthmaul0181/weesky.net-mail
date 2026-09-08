using System.Text;
using System.Xml.Linq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using CalendarRow = weesky.Snoopy.Microservice.Data.Preferences.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// PUT on an event, each line of the spec's § 10 grid, over the real writer and this server's own
/// database: the status, the named precondition, and what the rows say afterwards.
/// </summary>
public sealed class CalDavPutTests : IAsyncLifetime
{
    private DavTestServer server = null!;
    private Guid calendarId;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync();
        calendarId = GivenCalendar("work");
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task ACreatingPut_Answers201WithItsEtag()
    {
        var response = await Put(Href("a.ics"), Event("u1"));

        Assert.Equal(201, response.StatusCode);
        Assert.NotNull(response.Header("ETag"));
        Assert.Equal(DavHeaders.ComplianceClasses, response.Header("DAV"));
    }

    [Fact]
    public async Task AReplacingPut_Answers204WithItsEtag()
    {
        await Put(Href("a.ics"), Event("u1"));

        var response = await Put(Href("a.ics"), Event("u1", "Renamed"));

        // Always an ETag, unlike 4c's stamped card: the stored bytes are the sent ones.
        Assert.Equal(204, response.StatusCode);
        Assert.NotNull(response.Header("ETag"));
    }

    [Fact]
    public async Task WhatAPutStores_IsWhatAGetServes()
    {
        var ics = Event("u1", "Adèle du train");
        var put = await Put(Href("a.ics"), ics);

        var get = await server.SendAsync("GET", Href("a.ics"));

        Assert.Equal(Encoding.UTF8.GetBytes(ics), get.BodyBytes);
        Assert.Equal(put.Header("ETag"), get.Header("ETag"));
    }

    [Fact]
    public async Task ABodyPastTheRequestLimit_Answers413()
    {
        var response = await PutBytes(Href("a.ics"), new byte[2 * IcsGuards.MaxIcsBytes + 1]);

        Assert.Equal(413, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(EveryRefusalOfTheGrid))]
    public async Task EachLineOfTheGrid_IsRefusedWithItsOwnElement(string ics, string condition)
    {
        var response = await Put(Href("a.ics"), ics);

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CalDav + condition, ConditionOf(response));
        using var db = server.CreateContext();
        Assert.Empty(db.CalendarEvents);
    }

    public static TheoryData<string, string> EveryRefusalOfTheGrid() => new()
    {
        { Event("u1").Replace("VERSION:2.0", "VERSION:1.0"), "supported-calendar-data" },
        { Ics.Todo(), "supported-calendar-component" },
        { Ics.Events(("a", null)).Replace("END:VCALENDAR", "BEGIN:VTODO\r\nUID:a\r\nDTSTAMP:20260901T080000Z\r\nSUMMARY:Milk\r\nEND:VTODO\r\nEND:VCALENDAR"), "valid-calendar-object-resource" },
        { Ics.Events(("a", null), ("b", null)), "valid-calendar-object-resource" },
        { Ics.Events(("a", null), ("a", null)), "valid-calendar-object-resource" },
        // RFC 5545 § 3.6.1: a VEVENT owes a DTSTART, and none of the four other guards reads it.
        { EventWithoutStart(), "valid-calendar-data" },
        { Ics.DensityBomb(), "max-instances" },
        { "this is no calendar", "valid-calendar-data" },
        // Exactly one byte over the ceiling: the request limit sits above it, so the announced 403
        // answers and never the transport 413.
        { Ics.Padded(IcsGuards.MaxIcsBytes + 1), "max-resource-size" },
    };

    [Fact]
    public async Task AUidHeldByAnotherName_Answers403NoUidConflictWithItsHref()
    {
        await Put(Href("a.ics"), Event("shared"));

        var response = await Put(Href("b.ics"), Event("shared"));

        Assert.Equal(403, response.StatusCode);
        var condition = ErrorRootOf(response).Element(DavXml.CalDav + "no-uid-conflict")!;
        Assert.Equal(Href("a.ics"), condition.Element(DavXml.Href)!.Value);
    }

    [Fact]
    public async Task AnIfNoneMatchStar_OnAnExistingResource_Answers412AndArchives()
    {
        await Put(Href("a.ics"), Event("u1"));
        var refused = Event("u1", "Refused");

        var response = await Put(Href("a.ics"), refused, ifNoneMatch: "*");

        Assert.Equal(412, response.StatusCode);
        using var db = server.CreateContext();
        Assert.Equal(refused, Assert.Single(db.CalendarRevisions.Where(r => r.Cause == RevisionCause.Rejected)).IcsRaw);
        Assert.Equal("Standup", db.CalendarEvents.Single().Summary);
    }

    [Fact]
    public async Task AnIfNoneMatchStar_OnANewName_Creates() =>
        Assert.Equal(201, (await Put(Href("new.ics"), Event("u1"), ifNoneMatch: "*")).StatusCode);

    [Fact]
    public async Task AStaleIfMatch_Answers412TakesNoRankAndLeavesARejectedRevision()
    {
        await Put(Href("a.ics"), Event("u1"));
        var refused = Event("u1", "Written on a train");

        var response = await Put(Href("a.ics"), refused, ifMatch: "\"stale\"");

        Assert.Equal(412, response.StatusCode);
        using var db = server.CreateContext();
        Assert.Equal(1ul, db.CalendarSyncStates.Single(s => s.CalendarId == calendarId).Seq);
        Assert.Equal(refused, Assert.Single(db.CalendarRevisions.Where(r => r.Cause == RevisionCause.Rejected)).IcsRaw);
    }

    [Fact]
    public async Task AMatchingIfMatch_IsAccepted()
    {
        var etag = (await Put(Href("a.ics"), Event("u1"))).Header("ETag")!;

        Assert.Equal(204, (await Put(Href("a.ics"), Event("u1", "Renamed"), ifMatch: etag)).StatusCode);
    }

    [Fact]
    public async Task AnIfMatchOnAnAbsentResource_Answers412AndNot404() =>
        Assert.Equal(412, (await Put(Href("never.ics"), Event("u1"), ifMatch: "\"x\"")).StatusCode);

    [Theory]
    [InlineData("text/calendar; charset=utf-8")]
    [InlineData(null)]
    public async Task ACalendarOrAbsentContentType_IsNotAJudgeOfTheBody(string? contentType) =>
        // The body is the only remaining judge (4c decision 10) once the media type itself is
        // calendar or absent — a form type must not have MVC's form binder swallow the body either.
        Assert.Equal(201, (await Put(Href("a.ics"), Event("u1"), contentType: contentType)).StatusCode);

    [Fact]
    public async Task APutUnderAContentTypeThatIsNotCalendar_NamesTheMediaType()
    {
        // RFC 4791 § 5.3.2.1: supported-calendar-data is « MUST be a supported media type », a
        // different refusal from « the data is invalid » — and the client acts differently on each.
        var response = await Put(Href("x.ics"), "<?xml version=\"1.0\"?><nope/>", contentType: "text/xml");

        Assert.Equal(403, response.StatusCode);
        Assert.Contains("supported-calendar-data", response.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMediaTypeThatOnlyStartsWithCalendar_IsStillRefused()
    {
        // StartsWith would let "text/calendarish" through: the media type must match exactly, once
        // its parameters are stripped, or a refusal a letter can walk around is not a refusal.
        var response = await Put(Href("z.ics"), Event("u1"), contentType: "text/calendarish");

        Assert.Equal(403, response.StatusCode);
        Assert.Contains("supported-calendar-data", response.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APutWithNoContentTypeAtAll_IsJudgedOnItsBody()
    {
        // Not every client sends one, and refusing on absence would break them for nothing.
        var response = await Put(Href("y.ics"), Event("u1", "sans en-tete"), contentType: null);

        Assert.Equal(201, response.StatusCode);
    }

    [Fact]
    public async Task ABodyThatIsNotStrictUtf8_Answers403ValidCalendarData()
    {
        var response = await PutBytes(Href("a.ics"), Encoding.Latin1.GetBytes(Event("u1", "Adèle")));

        // Decoded with a replacement fallback, the stored bytes would differ from the sent ones
        // and the ETag would lie.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CalDav + "valid-calendar-data", ConditionOf(response));
    }

    [Fact]
    public async Task ABodyOpeningOnAUtf8Signature_IsStored()
    {
        // The shape every Windows exporter writes. Refused until now with "the body is not
        // iCalendar text", on a file that is exactly that.
        var body = "\uFEFF" + Event("u1", "conseil d'administration");

        var response = await Put(Href("bom.ics"), body);

        Assert.Equal(201, response.StatusCode);
    }

    [Fact]
    public async Task APutDirectlyUnderTheHome_Answers403LocationOk()
    {
        var response = await Put($"{DavPaths.CalendarHome(UserId)}x", Event("u1"));

        // RFC 4791 § 5.3.2.1: not inside any calendar — and not a 308 either, since a resource is
        // what is being addressed, not a collection.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CalDav + "calendar-collection-location-ok", ConditionOf(response));
    }

    [Fact]
    public async Task APutOnACalendarItself_Answers403LocationOkToo()
    {
        var response = await Put(DavPaths.Calendar(UserId, "work"), Event("u1"));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CalDav + "calendar-collection-location-ok", ConditionOf(response));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("..%5C..%5Cetc")]
    [InlineData("a.ics%20")]
    public async Task AnInvalidName_IsRefusedByAConsideredAnswer_NotByRouting(string escaped)
    {
        var response = await Put($"{DavPaths.Calendar(UserId, "work")}{escaped}", Event("u1"));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CalDav + "valid-calendar-data", ConditionOf(response));
    }

    [Fact]
    public async Task AnUnknownCalendar_Answers404()
    {
        var response = await Put(DavPaths.Event(UserId, "nowhere", "a.ics"), Event("u1"));

        Assert.Equal(404, response.StatusCode);
        Assert.Equal(string.Empty, response.Body);
    }

    [Fact]
    public async Task AnotherUsersCalendar_Answers404() =>
        Assert.Equal(404, (await Put(DavPaths.Event(Guid.NewGuid(), "work", "a.ics"), Event("u1"))).StatusCode);

    [Fact]
    public async Task AFullCalendar_Answers507()
    {
        GivenTheCalendarIsFull();

        var response = await Put(Href("a.ics"), Event("u1"));

        // RFC 4918 § 11.5 — no CalDAV precondition names the cap, so the status carries it alone.
        Assert.Equal(507, response.StatusCode);
        Assert.Equal(string.Empty, response.Body);
    }

    [Fact]
    public async Task APutOnTheHome_Answers405()
    {
        var response = await Put(DavPaths.CalendarHome(UserId), Event("u1"));

        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.CalendarHomeAllow, response.Header("Allow"));
    }

    [Fact]
    public async Task CalDavSwitchedOff_Is403WithoutABody()
    {
        await using var off = await DavTestServer.StartAsync(calDav: false);

        var response = await Put(DavPaths.Event(off.UserId, "work", "a.ics"), Event("u1"), on: off);

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(string.Empty, response.Body);
    }

    [Fact]
    public async Task ACreatedResource_IsListedUnderTheCalendarAtTheRankItTook()
    {
        await Put(Href("a.ics"), Event("u1"));

        using var db = server.CreateContext();
        var row = db.CalendarEvents.Single();
        Assert.Equal(1ul, row.SyncSequence);
        Assert.Equal(1ul, db.CalendarSyncStates.Single(s => s.CalendarId == calendarId).Seq);
        Assert.Equal(calendarId, row.CalendarId);
        Assert.Equal(UserId, row.UserId);
    }

    private string Href(string davName) => DavPaths.Event(UserId, "work", davName);

    private Task<DavTestResponse> Put(string path, string body, string? ifMatch = null,
        string? ifNoneMatch = null, string? contentType = "text/calendar", DavTestServer? on = null) =>
        PutBytes(path, Encoding.UTF8.GetBytes(body), ifMatch, ifNoneMatch, contentType, on);

    private async Task<DavTestResponse> PutBytes(string path, byte[] body, string? ifMatch = null,
        string? ifNoneMatch = null, string? contentType = "text/calendar", DavTestServer? on = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        var content = new ByteArrayContent(body);
        if (contentType is not null) content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        request.Content = content;
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (ifNoneMatch is not null) request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);

        using var response = await (on ?? server).Client.SendAsync(request);
        return await DavTestResponse.ReadAsync(response);
    }

    private Guid GivenCalendar(string davName)
    {
        using var db = server.CreateContext();
        var row = new CalendarRow
        {
            Id = Guid.NewGuid(), UserId = UserId, DavName = davName, DisplayName = "Work",
            Description = string.Empty, Color = "#336699", Order = 1,
            TimeZone = "Europe/Brussels", IsVisible = true,
        };
        db.Calendars.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    private void GivenTheCalendarIsFull()
    {
        using var db = server.CreateContext();
        for (var i = 0; i < CalendarEventStore.MaxPerCalendar; i++)
        {
            db.CalendarEvents.Add(new CalendarEvent
            {
                Id = Guid.NewGuid(), CalendarId = calendarId, UserId = UserId,
                Uid = Guid.NewGuid().ToString(), DavName = $"filler{i}.ics", IcsRaw = "x",
                IcsHash = "h", SyncSequence = 1, UpdatedAt = DateTime.UtcNow,
            });
        }

        db.SaveChanges();
    }

    internal static string Event(string uid, string summary = "Standup") =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//weesky//tests//EN\r\n"
        + $"BEGIN:VEVENT\r\nUID:{uid}\r\nDTSTAMP:20260901T080000Z\r\n"
        + "DTSTART:20260907T090000Z\r\nDTEND:20260907T100000Z\r\n"
        + $"SUMMARY:{summary}\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    internal static string EventWithoutStart() =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//weesky//tests//EN\r\n"
        + "BEGIN:VEVENT\r\nUID:nostart\r\nDTSTAMP:20260901T080000Z\r\nSUMMARY:No start\r\nEND:VEVENT\r\n"
        + "END:VCALENDAR\r\n";

    private static XElement ErrorRootOf(DavTestResponse response)
    {
        var root = XDocument.Parse(response.Body).Root!;
        Assert.Equal(DavXml.Error, root.Name);
        return root;
    }

    private static XName ConditionOf(DavTestResponse response) =>
        Assert.Single(ErrorRootOf(response).Elements()).Name;
}
