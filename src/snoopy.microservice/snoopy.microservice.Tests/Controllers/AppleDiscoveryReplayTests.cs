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
/// Apple's pairing sequence replayed in process, no device involved.
/// <para>What this proves: our answers do not make that sequence fall over, and a property one of
/// these clients asks for but we do not serve comes back as a <c>404</c> propstat rather than a
/// <c>500</c> or a silence. What it does not prove: that an iPhone or a Mac makes anything of what
/// comes back — nothing here is plugged into a device, and the campaign has none.</para>
/// <para>Every replayed body carries where it comes from: a path in the pinned
/// ccs-caldavtester clone, or a named public source. A body with no provenance is a guess dressed
/// as a test, which is the one thing this layer exists not to be.</para>
/// </summary>
public sealed class AppleDiscoveryReplayTests : IAsyncLifetime
{
    /// <summary>Apple's own namespace for the bulk-requests property; nothing of ours is served
    /// from it, so no constant names it.</summary>
    private static readonly XNamespace MeCom = "http://me.com/_namespace/";

    /// <summary>Total order on XName, so two property lists compare as sets without a set type.
    /// </summary>
    private static readonly IComparer<XName> XNameOrder =
        Comparer<XName>.Create((left, right) =>
            string.CompareOrdinal(left.ToString(), right.ToString()));

    private DavTestServer server = null!;
    private Guid calendarId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync();
        calendarId = Guid.NewGuid();
        using var db = server.CreateContext();
        db.Calendars.Add(new CalendarRow
        {
            Id = calendarId,
            UserId = server.UserId,
            DavName = CalendarStore.DefaultDavName,
            DisplayName = "Personal",
            Description = "Ce que le webmail pose",
            Color = "#3b82c4",
            Order = 0,
            TimeZone = "Europe/Brussels",
            IsVisible = true,
        });
        // The state row every calendar is born with (5a, decision 2): without it sync-collection
        // refuses valid-sync-token, which is the answer to a lost row and not to an initial sync.
        db.CalendarSyncStates.Add(new CalendarSyncState
        {
            CalendarId = calendarId, Epoch = Guid.NewGuid(), Seq = 0, PrunedBelow = 0,
        });
        db.SaveChanges();
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Theory]
    [InlineData("/")]
    [InlineData(DavPaths.Root + "/")]
    public async Task PropfindDepthZero_OnARoot_Answers401WithoutCredentials(string path)
    {
        // The first request iOS sends is a PROPFIND on the bare host, before .well-known and
        // before the SRV lookup (stalwart discussion #3259, a user's trace — the only source).
        var response = await server.SendUnauthenticated("PROPFIND", path, depth: "0");

        Assert.Equal(401, response.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData(DavPaths.Root + "/")]
    public async Task PropfindDepthZero_OnARoot_AnswersTheCurrentUserPrincipal(string path)
    {
        var response = await server.PropfindAsync(path, "0",
            new XElement(DavXml.Dav + "propfind",
                new XElement(DavXml.Prop, new XElement(DavXml.Dav + "current-user-principal")))
                .ToString());

        Assert.Equal(207, response.StatusCode);
        Assert.Equal(DavPaths.Principal(server.UserId),
            XDocument.Parse(response.Body)
                .Descendants(DavXml.Dav + "current-user-principal").Single()
                .Element(DavXml.Href)!.Value);
    }

    [Theory]
    [InlineData("/.well-known/caldav")]
    [InlineData("/.well-known/caldav/")]
    public async Task PropfindOnTheWellKnown_Redirects_TrailingSlashOrNot(string path)
    {
        // PROPFIND, not GET: this is how iOS, DAVx5 and Thunderbird open discovery, and the
        // trailing-slash form is the one ccs-caldavtester sends — untested until now.
        var response = await server.SendUnauthenticated("PROPFIND", path);

        Assert.Equal(301, response.StatusCode);
        Assert.Equal(DavPaths.Root + "/", response.Header("Location"));
    }

    /// <summary>Resource/CalDAV/ical-client/client1/1.xml — iOS, verbatim.</summary>
    private const string IosPrincipalBody = """
        <?xml version="1.0" encoding="utf-8"?>
        <x0:propfind xmlns:x2="http://calendarserver.org/ns/" xmlns:x1="urn:ietf:params:xml:ns:caldav" xmlns:x0="DAV:">
         <x0:prop>
          <x1:calendar-home-set/>
          <x1:calendar-user-address-set/>
          <x1:schedule-inbox-URL/>
          <x1:schedule-outbox-URL/>
          <x2:dropbox-home-URL/>
          <x2:notifications-URL/>
          <x0:displayname/>
         </x0:prop>
        </x0:propfind>
        """;

    /// <summary>Resource/CalDAV/ical-client/client2/1.xml — iCal 10.6, verbatim. Same list, with
    /// notification-URL in the singular, plus three DAV: properties.</summary>
    private const string ICalPrincipalBody = """
        <?xml version="1.0" encoding="utf-8"?>
        <x0:propfind xmlns:x1="urn:ietf:params:xml:ns:caldav" xmlns:x0="DAV:" xmlns:x2="http://calendarserver.org/ns/">
         <x0:prop>
          <x0:principal-collection-set/>
          <x1:calendar-home-set/>
          <x1:calendar-user-address-set/>
          <x1:schedule-inbox-URL/>
          <x1:schedule-outbox-URL/>
          <x2:dropbox-home-URL/>
          <x2:notification-URL/>
          <x0:displayname/>
          <x0:principal-URL/>
          <x0:supported-report-set/>
         </x0:prop>
        </x0:propfind>
        """;

    [Fact]
    public async Task TheIosPrincipalBody_404sWhatWeDoNotServe_RatherThan500OrSilence()
    {
        var response = await server.PropfindAsync(DavPaths.Principal(server.UserId), "0", IosPrincipalBody);

        Assert.Equal(207, response.StatusCode);
        var only = TheOnlyResponse(response);
        Assert.Contains(DavXml.CalDav + "calendar-home-set", NamesIn(only, 200));
        Assert.Contains(DavXml.Dav + "displayname", NamesIn(only, 200));
        // Nothing here schedules, and there is no dropbox nor a notification collection.
        Assert.Equal(
            [DavXml.CalDav + "schedule-inbox-URL", DavXml.CalDav + "schedule-outbox-URL",
             DavXml.CalendarServer + "dropbox-home-URL", DavXml.CalendarServer + "notifications-URL"],
            NamesIn(only, 404));
    }

    [Fact]
    public async Task TheICalPrincipalBody_404sTheSameFour_WithNotificationUrlInTheSingular()
    {
        var response = await server.PropfindAsync(DavPaths.Principal(server.UserId), "0", ICalPrincipalBody);

        Assert.Equal(207, response.StatusCode);
        var only = TheOnlyResponse(response);
        Assert.Equal(
            [DavXml.CalDav + "schedule-inbox-URL", DavXml.CalDav + "schedule-outbox-URL",
             DavXml.CalendarServer + "dropbox-home-URL", DavXml.CalendarServer + "notification-URL"],
            NamesIn(only, 404));
        Assert.Contains(DavXml.Dav + "principal-URL", NamesIn(only, 200));
        Assert.Contains(DavXml.Dav + "supported-report-set", NamesIn(only, 200));
    }

    /// <summary>Resource/CalDAV/ical-client/client1/2.xml — iOS, verbatim, seven properties.</summary>
    private const string IosHomeBody = """
        <?xml version="1.0" encoding="utf-8"?>
        <x0:propfind xmlns:x1="http://calendarserver.org/ns/" xmlns:x0="DAV:" xmlns:x3="http://apple.com/ns/ical/" xmlns:x2="urn:ietf:params:xml:ns:caldav">
         <x0:prop>
          <x1:getctag/>
          <x0:displayname/>
          <x2:calendar-description/>
          <x3:calendar-color/>
          <x3:calendar-order/>
          <x0:resourcetype/>
          <x2:calendar-free-busy-set/>
         </x0:prop>
        </x0:propfind>
        """;

    /// <summary>Resource/CalDAV/ical-client/client2/2.xml — iCal 10.6, verbatim, twenty-six
    /// properties.</summary>
    private const string ICalHomeBody = """
        <?xml version="1.0" encoding="utf-8"?>
        <x0:propfind xmlns:x0="DAV:" xmlns:x3="http://apple.com/ns/ical/" xmlns:x1="http://calendarserver.org/ns/" xmlns:x2="urn:ietf:params:xml:ns:caldav">
         <x0:prop>
          <x1:xmpp-server/>
          <x1:xmpp-uri/>
          <x1:getctag/>
          <x0:displayname/>
          <x2:calendar-description/>
          <x3:calendar-color/>
          <x3:calendar-order/>
          <x2:supported-calendar-component-set/>
          <x0:resourcetype/>
          <x0:owner/>
          <x2:calendar-free-busy-set/>
          <x2:schedule-calendar-transp/>
          <x2:schedule-default-calendar-URL/>
          <x0:quota-available-bytes/>
          <x0:quota-used-bytes/>
          <x2:calendar-timezone/>
          <x0:current-user-privilege-set/>
          <x1:source/>
          <x1:subscribed-strip-alarms/>
          <x1:subscribed-strip-attachments/>
          <x1:subscribed-strip-todos/>
          <x3:refreshrate/>
          <x1:push-transports/>
          <x1:pushkey/>
          <x1:publish-url/>
          <x1:allowed-sharing-modes/>
         </x0:prop>
        </x0:propfind>
        """;

    /// <summary>sabre.io/dav/clients/ical/, "iCal 10.9.2 Calendar Home Request", verbatim: the
    /// only published list carrying sync-token and the two default-alarm properties.</summary>
    private const string ICal1092HomeBody = """
        <?xml version='1.0' encoding='UTF-8'?>
        <A:propfind xmlns:A="DAV:">
          <A:prop>
            <A:add-member/>
            <C:allowed-sharing-modes xmlns:C="http://calendarserver.org/ns/"/>
            <D:autoprovisioned xmlns:D="http://apple.com/ns/ical/"/>
            <E:bulk-requests xmlns:E="http://me.com/_namespace/"/>
            <D:calendar-color xmlns:D="http://apple.com/ns/ical/"/>
            <B:calendar-description xmlns:B="urn:ietf:params:xml:ns:caldav"/>
            <B:calendar-free-busy-set xmlns:B="urn:ietf:params:xml:ns:caldav"/>
            <D:calendar-order xmlns:D="http://apple.com/ns/ical/"/>
            <B:calendar-timezone xmlns:B="urn:ietf:params:xml:ns:caldav"/>
            <A:current-user-privilege-set/>
            <B:default-alarm-vevent-date xmlns:B="urn:ietf:params:xml:ns:caldav"/>
            <B:default-alarm-vevent-datetime xmlns:B="urn:ietf:params:xml:ns:caldav"/>
            <A:displayname/>
            <C:getctag xmlns:C="http://calendarserver.org/ns/"/>
            <D:language-code xmlns:D="http://apple.com/ns/ical/"/>
            <D:location-code xmlns:D="http://apple.com/ns/ical/"/>
            <A:owner/>
            <C:pre-publish-url xmlns:C="http://calendarserver.org/ns/"/>
            <C:publish-url xmlns:C="http://calendarserver.org/ns/"/>
            <C:push-transports xmlns:C="http://calendarserver.org/ns/"/>
            <C:pushkey xmlns:C="http://calendarserver.org/ns/"/>
            <A:quota-available-bytes/>
            <A:quota-used-bytes/>
            <D:refreshrate xmlns:D="http://apple.com/ns/ical/"/>
            <A:resource-id/>
            <A:resourcetype/>
            <B:schedule-calendar-transp xmlns:B="urn:ietf:params:xml:ns:caldav"/>
            <B:schedule-default-calendar-URL xmlns:B="urn:ietf:params:xml:ns:caldav"/>
            <C:source xmlns:C="http://calendarserver.org/ns/"/>
            <C:subscribed-strip-alarms xmlns:C="http://calendarserver.org/ns/"/>
            <C:subscribed-strip-attachments xmlns:C="http://calendarserver.org/ns/"/>
            <C:subscribed-strip-todos xmlns:C="http://calendarserver.org/ns/"/>
            <B:supported-calendar-component-set xmlns:B="urn:ietf:params:xml:ns:caldav"/>
            <B:supported-calendar-component-sets xmlns:B="urn:ietf:params:xml:ns:caldav"/>
            <A:supported-report-set/>
            <A:sync-token/>
          </A:prop>
        </A:propfind>
        """;

    [Fact]
    public async Task TheIosHomeBody_AnswersSixOfSevenOnACalendar_AndAlmostNothingOnTheHome()
    {
        var response = await server.PropfindAsync(DavPaths.CalendarHome(server.UserId), "1", IosHomeBody);

        Assert.Equal(207, response.StatusCode);
        var responses = ResponsesByHref(response);

        var home = responses[DavPaths.CalendarHome(server.UserId)];
        // The home's table serves four properties and none of these is among them — getctag
        // included, which is a calendar's property, never a home's.
        Assert.Equal([DavXml.Dav + "displayname", DavXml.Dav + "resourcetype"], NamesIn(home, 200));
        Assert.Contains(DavXml.CalendarServer + "getctag", NamesIn(home, 404));

        var calendar = responses[Calendar()];
        Assert.Equal(
            [DavXml.CalendarServer + "getctag", DavXml.Dav + "displayname",
             DavXml.CalDav + "calendar-description", DavXml.Apple + "calendar-color",
             DavXml.Apple + "calendar-order", DavXml.Dav + "resourcetype"],
            NamesIn(calendar, 200));
        Assert.Equal([DavXml.CalDav + "calendar-free-busy-set"], NamesIn(calendar, 404));
        // A ctag with no value would pass a false test for a measure.
        Assert.NotEmpty(calendar.Descendants(DavXml.CalendarServer + "getctag").Single().Value);
    }

    [Fact]
    public async Task TheICalHomeBody_ServesTenOfTwentySix_AndPutsTheOtherSixteenIn404()
    {
        var response = await server.PropfindAsync(DavPaths.CalendarHome(server.UserId), "1", ICalHomeBody);

        Assert.Equal(207, response.StatusCode);
        var calendar = ResponsesByHref(response)[Calendar()];

        Assert.Equal(
            [DavXml.CalendarServer + "getctag", DavXml.Dav + "displayname",
             DavXml.CalDav + "calendar-description", DavXml.Apple + "calendar-color",
             DavXml.Apple + "calendar-order", DavXml.CalDav + "supported-calendar-component-set",
             DavXml.Dav + "resourcetype", DavXml.Dav + "owner",
             DavXml.CalDav + "calendar-timezone", DavXml.Dav + "current-user-privilege-set"],
            NamesIn(calendar, 200));
        // No quota model, no scheduling, no push and no sharing: each says so in its own propstat.
        Assert.Equal(
            [DavXml.CalendarServer + "xmpp-server", DavXml.CalendarServer + "xmpp-uri",
             DavXml.CalDav + "calendar-free-busy-set", DavXml.CalDav + "schedule-calendar-transp",
             DavXml.CalDav + "schedule-default-calendar-URL", DavXml.Dav + "quota-available-bytes",
             DavXml.Dav + "quota-used-bytes", DavXml.CalendarServer + "source",
             DavXml.CalendarServer + "subscribed-strip-alarms",
             DavXml.CalendarServer + "subscribed-strip-attachments",
             DavXml.CalendarServer + "subscribed-strip-todos", DavXml.Apple + "refreshrate",
             DavXml.CalendarServer + "push-transports", DavXml.CalendarServer + "pushkey",
             DavXml.CalendarServer + "publish-url", DavXml.CalendarServer + "allowed-sharing-modes"],
            NamesIn(calendar, 404));
    }

    [Fact]
    public async Task TheICal1092HomeBody_ServesSyncToken_And404sTheDefaultAlarms_LosingNothing()
    {
        var response = await server.PropfindAsync(DavPaths.CalendarHome(server.UserId), "1", ICal1092HomeBody);

        Assert.Equal(207, response.StatusCode);
        var responses = ResponsesByHref(response);
        var calendar = responses[Calendar()];
        var served = NamesIn(calendar, 200);
        var absent = NamesIn(calendar, 404);

        Assert.Contains(DavXml.Dav + "sync-token", served);
        Assert.Contains(DavXml.CalDav + "default-alarm-vevent-datetime", absent);
        Assert.Contains(DavXml.CalDav + "default-alarm-vevent-date", absent);
        Assert.Contains(MeCom + "bulk-requests", absent);
        // Every asked name comes back in one propstat or the other: a silence is what makes a
        // client wait for ever for a value it believes is on its way.
        Assert.Equal(AskedIn(ICal1092HomeBody), [.. served.Concat(absent).Order(XNameOrder)]);

        // The home's table serves four names only, so a calendar's properties 404 there. Asserted
        // on this body alone, the sole iCal one asking for all three; the 10.6 body has no
        // sync-token and the iOS one already reads the home.
        var onTheHome = NamesIn(responses[DavPaths.CalendarHome(server.UserId)], 404);
        Assert.Contains(DavXml.Dav + "owner", onTheHome);
        Assert.Contains(DavXml.Dav + "sync-token", onTheHome);
        Assert.Contains(DavXml.Apple + "calendar-color", onTheHome);
    }

    [Fact]
    public async Task AnInitialSyncCollection_ThenAMultigetOnWhatItReturns()
    {
        // RFC 6578 § 3.4's initial synchronisation: an EMPTY sync-token. No Apple sync-collection
        // body is public, so the shape is the one Thunderbird sends, sync-level included: § 3 makes
        // it mandatory, and no Depth header is sent here for want of a source showing Apple's.
        GivenEvent("a.ics", Ics.Single("DTSTART:20260907T090000Z", "DTEND:20260907T100000Z"));
        var body = new XElement(DavXml.Dav + "sync-collection",
            new XElement(DavXml.Dav + "sync-token"),
            new XElement(DavXml.Dav + "sync-level", "1"),
            new XElement(DavXml.Prop, new XElement(DavXml.Dav + "getetag")));

        var synced = await server.SendAsync("REPORT", Calendar(), body.ToString());

        Assert.Equal(207, synced.StatusCode);
        var href = XDocument.Parse(synced.Body).Descendants(DavXml.Href)
            .Select(h => h.Value).First(v => v.EndsWith("a.ics", StringComparison.Ordinal));

        var multiget = new XElement(DavXml.CalDav + "calendar-multiget",
            new XElement(DavXml.Prop, new XElement(DavXml.Dav + "getetag"),
                new XElement(DavXml.CalDav + "calendar-data")),
            new XElement(DavXml.Href, href));

        var fetched = await server.SendAsync("REPORT", Calendar(), multiget.ToString());

        Assert.Equal(207, fetched.StatusCode);
        // A name we do not serve is written as an EMPTY element in the 404 propstat, so counting
        // the elements would still find one were calendar-data to stop being served: the propstat
        // it sits in and the content it carries are what make this bite.
        var member = TheOnlyResponse(fetched);
        Assert.Contains(DavXml.CalDav + "calendar-data", NamesIn(member, 200));
        var data = Assert.Single(member.Descendants(DavXml.CalDav + "calendar-data"));
        Assert.Contains("BEGIN:VCALENDAR", data.Value, StringComparison.Ordinal);
        Assert.Contains("UID:single", data.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheIosInitialLoad_ATimeRangeWithStartAlone_ServesAnEventBeyondFiveYears()
    {
        // The shape of the only published iOS calendar-query (sabre): a time-range on the VEVENT
        // comp-filter, start alone, and a prop asking getetag and resourcetype — nothing else.
        GivenEvent("passeport.ics",
            Ics.Single("DTSTART:20320315T090000Z", "DTEND:20320315T100000Z"));
        var body = new XElement(DavXml.CalDav + "calendar-query",
            new XElement(DavXml.Prop, new XElement(DavXml.Dav + "getetag"),
                new XElement(DavXml.Dav + "resourcetype")),
            new XElement(DavXml.CalDav + "filter",
                new XElement(DavXml.CalDav + "comp-filter", new XAttribute("name", "VCALENDAR"),
                    new XElement(DavXml.CalDav + "comp-filter", new XAttribute("name", "VEVENT"),
                        new XElement(DavXml.CalDav + "time-range",
                            new XAttribute("start", "20260907T000000Z"))))));

        var response = await server.SendAsync("REPORT", Calendar(), body.ToString());

        Assert.Equal(207, response.StatusCode);
        Assert.Contains("passeport.ics", response.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMacOsProppatch_ReadsTheColour_KeepsTheOrder_AndRefusesTheDefaultAlarm()
    {
        // calendar-color carries symbolic-color on iOS and macOS (stalwart #1611); 5c ignores the
        // attribute and reads the value. default-alarm-vevent-date is the body macOS sends the
        // moment default alerts are touched (sabre #935) — and the one we refuse.
        var body = new XElement(DavXml.Dav + "propertyupdate",
            new XElement(DavXml.Dav + "set", new XElement(DavXml.Prop,
                new XElement(DavXml.Apple + "calendar-color",
                    new XAttribute("symbolic-color", "green"), "#63DA38"),
                new XElement(DavXml.Apple + "calendar-order", "3"),
                new XElement(DavXml.CalDav + "default-alarm-vevent-date", "BEGIN:VALARM\r\nEND:VALARM"))));

        var response = await server.SendAsync("PROPPATCH", Calendar(), body.ToString());

        Assert.Equal(207, response.StatusCode);
        var only = TheOnlyResponse(response);
        Assert.Equal(
            [DavXml.Apple + "calendar-color", DavXml.Apple + "calendar-order"], NamesIn(only, 200));
        Assert.Equal([DavXml.CalDav + "default-alarm-vevent-date"], NamesIn(only, 403));

        using var db = server.CreateContext();
        var stored = db.Calendars.Single(c => c.DavName == CalendarStore.DefaultDavName);
        Assert.Equal("#63da38", stored.Color);
        Assert.Equal(3, stored.Order);
    }

    private string Calendar() => DavPaths.Calendar(server.UserId, CalendarStore.DefaultDavName);

    private void GivenEvent(string davName, string ics)
    {
        using var db = server.CreateContext();
        var row = CalDavQueryTests.Row(server.UserId, calendarId, davName, ics,
            IcsProjector.Project(IcsDocument.TryLoad(ics)!, Ics.Zone));
        db.CalendarEvents.Add(row);
        // The writer stamps a row with the counter it has just advanced, so the state follows here
        // too: a member ranked above it is one no sync-collection window ever reaches.
        db.CalendarSyncStates.Single(s => s.CalendarId == calendarId).Seq = row.SyncSequence;
        db.SaveChanges();
    }

    /// <summary>The property names of one propstat of one response, in document order.</summary>
    private static List<XName> NamesIn(XElement response, int status) =>
    [
        .. response.Elements(DavXml.PropStat)
            .Where(propstat => propstat.Element(DavXml.Status)!.Value
                .Contains($" {status} ", StringComparison.Ordinal))
            .SelectMany(propstat => propstat.Element(DavXml.Prop)!.Elements().Select(e => e.Name)),
    ];

    private static XElement TheOnlyResponse(DavTestResponse response) =>
        XDocument.Parse(response.Body).Descendants(DavXml.Response).Single();

    /// <summary>A Depth: 1 answers one response per resource and the expectations differ between
    /// them, so every assertion here picks its response by href rather than by rank.</summary>
    private static Dictionary<string, XElement> ResponsesByHref(DavTestResponse response) =>
        XDocument.Parse(response.Body).Descendants(DavXml.Response)
            .ToDictionary(r => r.Element(DavXml.Href)!.Value);

    private static List<XName> AskedIn(string body) =>
        [.. XDocument.Parse(body).Descendants(DavXml.Prop).Single().Elements()
            .Select(e => e.Name).Order(XNameOrder)];
}
