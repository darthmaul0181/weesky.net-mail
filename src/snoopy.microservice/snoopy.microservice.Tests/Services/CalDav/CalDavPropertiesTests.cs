using System.Globalization;
using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services.CalDav;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Services.CalDav;

/// <summary>The closed property set of the calendar tree, property by property: what a client's
/// screen reads, and what an <c>allprop</c> deliberately leaves out.</summary>
public sealed class CalDavPropertiesTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly DavCalendar Calendar = new(
        Guid.NewGuid(), UserId, "work", "Work", "What pays", "#336699", 3, "Europe/Brussels");

    private static readonly DavEvent Event = new(
        Guid.NewGuid(), Calendar.Id, "a.ics", "uid-1", "BEGIN:VCALENDAR\r\nSUMMARY:Adá\r\nEND:VCALENDAR\r\n",
        "abc123", new DateTime(2026, 8, 24, 13, 5, 0, DateTimeKind.Utc), 4);

    private static readonly SyncState State = new(Guid.Parse("55555555-5555-5555-5555-555555555555"), 12, 0);

    [Fact]
    public void TheCalendarNamesItselfACalendar()
    {
        var resourceType = Found(CalendarResource(), DavXml.Dav + "resourcetype")!;

        Assert.NotNull(resourceType.Element(DavXml.Dav + "collection"));
        Assert.NotNull(resourceType.Element(DavXml.CalDav + "calendar"));
    }

    [Fact]
    public void TheCalendarAnswersItsOwnColumns()
    {
        var resource = CalendarResource();

        Assert.Equal("Work", Found(resource, DavXml.Dav + "displayname")!.Value);
        Assert.Equal("What pays", Found(resource, DavXml.CalDav + "calendar-description")!.Value);
        Assert.Equal("#336699", Found(resource, DavXml.Apple + "calendar-color")!.Value);
        Assert.Equal("3", Found(resource, DavXml.Apple + "calendar-order")!.Value);
    }

    [Fact]
    public void TheCalendarAnswersTheCeilingsTheStoreEnforces()
    {
        var resource = CalendarResource();

        // The constants, never a literal recopied here: an announced value the store would violate
        // is paid for in refusals the client cannot explain.
        Assert.Equal(IcsGuards.MaxIcsBytes.ToString(CultureInfo.InvariantCulture),
            Found(resource, DavXml.CalDav + "max-resource-size")!.Value);
        Assert.Equal(IcsGuards.MaxInstancesPerYear.ToString(CultureInfo.InvariantCulture),
            Found(resource, DavXml.CalDav + "max-instances")!.Value);
    }

    [Fact]
    public void TheCalendarAnnouncesVeventAloneAndTheTwoCollations()
    {
        var resource = CalendarResource();

        var components = Found(resource, DavXml.CalDav + "supported-calendar-component-set")!;
        Assert.Equal("VEVENT", Assert.Single(components.Elements(DavXml.CalDav + "comp"))
            .Attribute("name")?.Value);
        var data = Assert.Single(
            Found(resource, DavXml.CalDav + "supported-calendar-data")!.Elements());
        Assert.Equal("text/calendar", data.Attribute("content-type")?.Value);
        Assert.Equal("2.0", data.Attribute("version")?.Value);
        Assert.Equal([DavCollation.AsciiCasemap, DavCollation.Octet],
            Found(resource, DavXml.CalDav + "supported-collation-set")!.Elements().Select(e => e.Value));
    }

    [Fact]
    public void TheCalendarAnnouncesTheFiveReports()
    {
        var reports = Found(CalendarResource(), DavXml.Dav + "supported-report-set")!
            .Descendants(DavXml.Dav + "report")
            .Select(report => report.Elements().Single().Name);

        // The table of the spec § 6, announced from this slice on: the division into tasks is not
        // a state a client ever sees.
        Assert.Equal(
        [
            DavXml.CalDav + "calendar-multiget", DavXml.CalDav + "calendar-query",
            DavXml.CalDav + "free-busy-query", DavXml.Dav + "sync-collection",
            DavXml.Dav + "expand-property",
        ], reports);
    }

    [Fact]
    public void TheCalendarAnswersTheCounterOfItsOwnCollection()
    {
        var resource = CalendarResource();

        Assert.Equal(DavSyncToken.Ctag(State),
            Found(resource, DavXml.CalendarServer + "getctag")!.Value);
        Assert.Equal(DavSyncToken.Token(State), Found(resource, DavXml.Dav + "sync-token")!.Value);
    }

    [Fact]
    public void CalendarTimeZone_CarriesTheOneBlockOfTheCalendarsZone()
    {
        var value = Found(CalendarResource(), DavXml.CalDav + "calendar-timezone")!.Value;

        var reloaded = IcsDocument.TryLoad(value);
        Assert.NotNull(reloaded);
        Assert.Equal("Europe/Brussels", Assert.Single(reloaded.TimeZones).TzId);
    }

    [Fact]
    public void CalendarTimeZone_TakesItsLowerBoundFromTheClockItIsGiven()
    {
        // Deliberately in the past: Ical.Net walks the zone up to the real now, so a clock set
        // ahead of it throws — and a year behind is what makes a table reading DateTime.UtcNow
        // produce a different document from this one.
        var clock = new MutableTimeProvider { Now = new DateTimeOffset(2020, 6, 1, 0, 0, 0, TimeSpan.Zero) };

        var value = Found(CalendarResource(), DavXml.CalDav + "calendar-timezone", clock)!.Value;

        var calendar = new IcsCalendar();
        calendar.AddTimeZone(IcsTimeZones.Emit(
            "Europe/Brussels", new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(IcsDocument.Serialize(calendar), value);
    }

    [Fact]
    public void TheEventAnswersWhatAClientReadsOffAMember()
    {
        var resource = EventResource();

        Assert.Equal("\"abc123\"", Found(resource, DavXml.Dav + "getetag")!.Value);
        Assert.Equal(DavHeaders.CalendarContentType,
            Found(resource, DavXml.Dav + "getcontenttype")!.Value);
        // UTF-8 bytes, never characters: the accented summary is what makes the two differ.
        Assert.Equal(45, Event.IcsRaw.Length);
        Assert.Equal("46", Found(resource, DavXml.Dav + "getcontentlength")!.Value);
        Assert.Equal("Mon, 24 Aug 2026 13:05:00 GMT",
            Found(resource, DavXml.Dav + "getlastmodified")!.Value);
        Assert.Empty(Found(resource, DavXml.Dav + "resourcetype")!.Elements());
        Assert.Equal(Event.IcsRaw, Found(resource, DavXml.CalDav + "calendar-data")!.Value);
    }

    [Fact]
    public void AnAllpropOnAnEvent_LeavesOutCalendarData()
    {
        var (found, _) = CalDavProperties.Resolve(
            AllProp(), EventResource(), new MutableTimeProvider());

        // RFC 4791 § 9.6 makes calendar-data a property only a client naming it may have — and an
        // allprop over a full collection would otherwise carry every file of it.
        Assert.DoesNotContain(found, element => element.Name == DavXml.CalDav + "calendar-data");
        Assert.Contains(found, element => element.Name == DavXml.Dav + "getetag");
        Assert.DoesNotContain(found, element => element.Name == DavXml.Dav + "current-user-privilege-set");
    }

    [Fact]
    public void APropnameOnAnEvent_StillListsCalendarData()
    {
        var (found, _) = CalDavProperties.Resolve(
            new DavPropertyRequest(DavPropertyMode.PropName, []), EventResource(),
            new MutableTimeProvider());

        // propname says what the resource carries; leaving it out would mean the client never asks.
        Assert.Contains(found, element => element.Name == DavXml.CalDav + "calendar-data");
    }

    [Fact]
    public void ACalendarWhoseZoneTheTzdbLostAnswers404_NotA500()
    {
        var lost = new DavResourceContext(
            DavResourceKind.Calendar, UserId, "someone@weesky.be", null, State,
            CollectionName: "work", Calendar: Calendar with { TimeZone = "Mars/Olympus" });

        var (found, missing) = CalDavProperties.Resolve(AllProp(), lost, new MutableTimeProvider());

        // Ical.Net refuses an id NodaTime does not hold, and allprop pours this property: one
        // hand-edited row would otherwise 500 the whole Depth: 1 of the home.
        Assert.DoesNotContain(found, e => e.Name == DavXml.CalDav + "calendar-timezone");
        Assert.Contains(DavXml.CalDav + "calendar-timezone", missing);
        Assert.Contains(found, e => e.Name == DavXml.Dav + "displayname");
    }

    [Fact]
    public void AnAllpropOnACalendar_StillLeavesOutTheSyncToken()
    {
        var (found, _) = CalDavProperties.Resolve(
            AllProp(), CalendarResource(), new MutableTimeProvider());

        Assert.DoesNotContain(found, element => element.Name == DavXml.Dav + "sync-token");
        Assert.Contains(found, element => element.Name == DavXml.CalendarServer + "getctag");
    }

    [Fact]
    public void TheHomeAnnouncesExpandPropertyAlone()
    {
        var resource = new DavResourceContext(
            DavResourceKind.CalendarHome, UserId, "someone@weesky.be", null, null);

        Assert.Equal("Calendars", Found(resource, DavXml.Dav + "displayname")!.Value);
        Assert.Equal(DavXml.Dav + "expand-property",
            Assert.Single(Found(resource, DavXml.Dav + "supported-report-set")!
                .Descendants(DavXml.Dav + "report")).Elements().Single().Name);
    }

    [Fact]
    public void TheCollectionOfHomes_IsAnIntermediateOne()
    {
        var resource = new DavResourceContext(
            DavResourceKind.CalendarCollection, UserId, "someone@weesky.be", null, null);

        Assert.Equal("Calendar Homes", Found(resource, DavXml.Dav + "displayname")!.Value);
    }

    /// <summary>
    /// Every property the four calendar tables serve, and no other. Written here rather than read
    /// from the tables, so that a row added, removed, renamed or renamespaced turns a case red
    /// instead of passing unnoticed — the pincer CardDavPropertiesTests holds on the book.
    /// </summary>
    private static readonly (DavResourceKind Kind, XName Name)[] ClosedSet =
    [
        (DavResourceKind.CalendarCollection, DavXml.Dav + "resourcetype"),
        (DavResourceKind.CalendarCollection, DavXml.Dav + "displayname"),
        (DavResourceKind.CalendarCollection, DavXml.Dav + "current-user-principal"),
        (DavResourceKind.CalendarCollection, DavXml.Dav + "principal-collection-set"),
        (DavResourceKind.CalendarCollection, DavXml.Dav + "supported-report-set"),

        (DavResourceKind.CalendarHome, DavXml.Dav + "resourcetype"),
        (DavResourceKind.CalendarHome, DavXml.Dav + "displayname"),
        (DavResourceKind.CalendarHome, DavXml.Dav + "supported-report-set"),
        (DavResourceKind.CalendarHome, DavXml.Dav + "current-user-principal"),

        (DavResourceKind.Calendar, DavXml.Dav + "resourcetype"),
        (DavResourceKind.Calendar, DavXml.Dav + "displayname"),
        (DavResourceKind.Calendar, DavXml.CalDav + "calendar-description"),
        (DavResourceKind.Calendar, DavXml.Apple + "calendar-color"),
        (DavResourceKind.Calendar, DavXml.Apple + "calendar-order"),
        (DavResourceKind.Calendar, DavXml.CalDav + "calendar-timezone"),
        (DavResourceKind.Calendar, DavXml.CalDav + "supported-calendar-component-set"),
        (DavResourceKind.Calendar, DavXml.CalDav + "supported-calendar-data"),
        (DavResourceKind.Calendar, DavXml.CalDav + "supported-collation-set"),
        (DavResourceKind.Calendar, DavXml.CalDav + "max-resource-size"),
        (DavResourceKind.Calendar, DavXml.CalDav + "max-instances"),
        (DavResourceKind.Calendar, DavXml.CalendarServer + "getctag"),
        (DavResourceKind.Calendar, DavXml.Dav + "sync-token"),
        (DavResourceKind.Calendar, DavXml.Dav + "supported-report-set"),
        (DavResourceKind.Calendar, DavXml.Dav + "current-user-privilege-set"),
        (DavResourceKind.Calendar, DavXml.Dav + "owner"),
        (DavResourceKind.Calendar, DavXml.Dav + "current-user-principal"),

        (DavResourceKind.Event, DavXml.Dav + "getetag"),
        (DavResourceKind.Event, DavXml.Dav + "getcontenttype"),
        (DavResourceKind.Event, DavXml.Dav + "getcontentlength"),
        (DavResourceKind.Event, DavXml.Dav + "getlastmodified"),
        (DavResourceKind.Event, DavXml.Dav + "resourcetype"),
        (DavResourceKind.Event, DavXml.Dav + "current-user-privilege-set"),
        (DavResourceKind.Event, DavXml.Dav + "supported-report-set"),
        (DavResourceKind.Event, DavXml.CalDav + "calendar-data"),
    ];

    public static TheoryData<string, string, string> EveryDeclaredProperty()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (kind, name) in ClosedSet)
            data.Add(kind.ToString(), name.NamespaceName, name.LocalName);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryDeclaredProperty))]
    public void EveryPropertyOfTheClosedSet_IsServedOnItsOwnKind(
        string kind, string ns, string localName)
    {
        var name = XNamespace.Get(ns) + localName;

        // A row silently dropped answers 404 to a client that reads it — and a calendar losing
        // current-user-privilege-set is a calendar Thunderbird shows read-only.
        var resolved = CalDavProperties.Resolve(
            new DavPropertyRequest(DavPropertyMode.Named, [name]), ResourceFor(Kind(kind)),
            new MutableTimeProvider());

        Assert.Empty(resolved.Missing);
        Assert.Equal(name, Assert.Single(resolved.Found).Name);
    }

    [Theory]
    [InlineData(nameof(DavResourceKind.CalendarCollection))]
    [InlineData(nameof(DavResourceKind.CalendarHome))]
    [InlineData(nameof(DavResourceKind.Calendar))]
    [InlineData(nameof(DavResourceKind.Event))]
    public void EachTable_DeclaresTheClosedSetAndNothingElse(string kind)
    {
        var resourceKind = Kind(kind);

        // propname answers the whole table, so this is the other half of the pincer: the theory
        // above catches a row removed, this one catches a row added without a decision.
        var declared = CalDavProperties
            .Resolve(new DavPropertyRequest(DavPropertyMode.PropName, []), ResourceFor(resourceKind),
                new MutableTimeProvider())
            .Found.Select(e => e.Name);

        Assert.Equal(
            Sorted(ClosedSet.Where(p => p.Kind == resourceKind).Select(p => p.Name)), Sorted(declared));
    }

    private static DavResourceKind Kind(string name) => Enum.Parse<DavResourceKind>(name);

    private static string[] Sorted(IEnumerable<XName> names) =>
        [.. names.Select(n => n.ToString()).OrderBy(n => n, StringComparer.Ordinal)];

    private static DavResourceContext ResourceFor(DavResourceKind kind) => kind switch
    {
        DavResourceKind.Calendar => CalendarResource(),
        DavResourceKind.Event => EventResource(),
        _ => new DavResourceContext(kind, UserId, "someone@weesky.be", null, null),
    };

    private static DavResourceContext CalendarResource() => new(
        DavResourceKind.Calendar, UserId, "someone@weesky.be", null, State,
        CollectionName: Calendar.DavName, Calendar: Calendar);

    private static DavResourceContext EventResource() => new(
        DavResourceKind.Event, UserId, "someone@weesky.be", null, null,
        CollectionName: Calendar.DavName, Calendar: Calendar, Event: Event);

    private static DavPropertyRequest AllProp() => new(DavPropertyMode.AllProp, []);

    private static XElement? Found(DavResourceContext resource, XName name, TimeProvider? clock = null)
    {
        var request = new DavPropertyRequest(DavPropertyMode.Named, [name]);
        var (found, _) = CalDavProperties.Resolve(
            request, resource, clock ?? new MutableTimeProvider());
        return found.SingleOrDefault();
    }
}
