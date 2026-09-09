using System.Xml.Linq;
using weesky.Snoopy.Microservice.Services.CalDav;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CalDav;

/// <summary>
/// What a PROPPATCH on a calendar is read as (§ 11): the five writable properties judged one by
/// one, everything else refused, and the one removal a calendar survives.
/// </summary>
public sealed class CalendarPropertyUpdateTests
{
    [Fact]
    public void TheFiveWritableOnes_AreAcceptedWithTheirValues()
    {
        var update = CalendarPropertyUpdate.Parse(Set(
            new XElement(DavXml.Dav + "displayname", "  Famille  "),
            new XElement(DavXml.CalDav + "calendar-description", "Les anniversaires"),
            new XElement(DavXml.Apple + "calendar-color", "#AABBCC80"),
            new XElement(DavXml.Apple + "calendar-order", "3"),
            new XElement(DavXml.CalDav + "calendar-timezone",
                MkCalendarRequestTests.Zones("Romance Standard Time"))));

        Assert.Empty(update.Refused);
        Assert.Equal("Famille", update.Accepted[DavXml.Dav + "displayname"]);
        Assert.Equal("Les anniversaires", update.Accepted[DavXml.CalDav + "calendar-description"]);
        // Apple's alpha channel dropped and the digits folded, by the store's own judge.
        Assert.Equal("#aabbcc", update.Accepted[DavXml.Apple + "calendar-color"]);
        Assert.Equal("3", update.Accepted[DavXml.Apple + "calendar-order"]);
        Assert.Equal("Europe/Paris", update.TimeZoneId);
    }

    [Fact]
    public void ATimeZoneCarryingAnything_ButOneVTimezone_NamesNoZone()
    {
        // RFC 4791 § 5.2.2 and § 9.8 both spell it « an iCalendar object with exactly one
        // VTIMEZONE component ». A VEVENT beside it is not that object, and storing its zone would
        // keep half of a body we refused to read.
        var zone = Ics.FixedZone("America/New_York", "-0500");

        Assert.Equal("America/New_York", CalendarPropertyValue.Zone(MkCalendarRequestTests.Zones("America/New_York")));
        Assert.Null(CalendarPropertyValue.Zone(Ics.Single("DTSTART:20260907T090000Z", null, zone: zone)));
    }

    [Fact]
    public void OneBadValue_IsRefusedOnItsOwnAndTheOthersStand()
    {
        var update = CalendarPropertyUpdate.Parse(Set(
            new XElement(DavXml.Dav + "displayname", "Famille"),
            new XElement(DavXml.Apple + "calendar-color", "rebeccapurple"),
            new XElement(DavXml.Apple + "calendar-order", "third")));

        // sabre applies property by property; a client that wrote one right value and one wrong
        // prefers the right one kept over the RFC's undo-it-all.
        Assert.Equal([DavXml.Apple + "calendar-color", DavXml.Apple + "calendar-order"],
            update.Refused);
        Assert.Equal("Famille", Assert.Single(update.Accepted).Value);
    }

    [Fact]
    public void AnythingWeDoNotStore_IsRefused()
    {
        var update = CalendarPropertyUpdate.Parse(Set(
            new XElement(DavXml.CalDav + "default-alarm-vevent-date", "BEGIN:VALARM"),
            new XElement(DavXml.Dav + "getetag", "\"x\"")));

        Assert.Empty(update.Accepted);
        Assert.Equal([DavXml.CalDav + "default-alarm-vevent-date", DavXml.Dav + "getetag"],
            update.Refused);
    }

    [Fact]
    public void RemovingTheDescription_EmptiesItAndIsAccepted()
    {
        var update = CalendarPropertyUpdate.Parse(Remove(
            new XElement(DavXml.CalDav + "calendar-description")));

        Assert.Null(update.Accepted[DavXml.CalDav + "calendar-description"]);
        Assert.Empty(update.Refused);
    }

    [Theory]
    [InlineData("DAV:", "displayname")]
    [InlineData("urn:ietf:params:xml:ns:caldav", "calendar-timezone")]
    [InlineData("http://apple.com/ns/ical/", "calendar-color")]
    public void RemovingAnythingElse_IsRefused(string ns, string localName)
    {
        var name = XNamespace.Get(ns) + localName;

        var update = CalendarPropertyUpdate.Parse(Remove(new XElement(name)));

        // A calendar always has a name, a colour, a rank and a zone.
        Assert.Empty(update.Accepted);
        Assert.Equal([name], update.Refused);
    }

    [Fact]
    public void AnEmptyDisplayName_IsRefusedRatherThanStored()
    {
        var update = CalendarPropertyUpdate.Parse(Set(new XElement(DavXml.Dav + "displayname", "  ")));

        Assert.Equal([DavXml.Dav + "displayname"], update.Refused);
    }

    [Fact]
    public void AnEmptyDescription_IsAcceptedAsAnEmptyOne()
    {
        var update = CalendarPropertyUpdate.Parse(Set(
            new XElement(DavXml.CalDav + "calendar-description", string.Empty)));

        Assert.Equal(string.Empty, update.Accepted[DavXml.CalDav + "calendar-description"]);
    }

    [Fact]
    public void APropertyNamedTwice_GetsOneAnswer()
    {
        var body = new XDocument(new XElement(DavXml.Dav + "propertyupdate",
            new XElement(DavXml.Dav + "set",
                new XElement(DavXml.Prop, new XElement(DavXml.Dav + "displayname", "First"))),
            new XElement(DavXml.Dav + "remove",
                new XElement(DavXml.Prop, new XElement(DavXml.Dav + "displayname")))));

        var update = CalendarPropertyUpdate.Parse(body);

        // § 9.2's propstat holds one entry per name: the first judgement stands.
        Assert.Equal("First", update.Accepted[DavXml.Dav + "displayname"]);
        Assert.Empty(update.Refused);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("propfind")]
    public void ABodyThatIsNoPropertyupdate_IsRefusedAsABadRequest(string? root)
    {
        var body = root is null ? null : new XDocument(new XElement(DavXml.Dav + root));

        Assert.Throws<DavBadRequestException>(() => CalendarPropertyUpdate.Parse(body));
    }

    [Fact]
    public void RemovingAPropertyThatWasNeverSet_IsNotAnError()
    {
        // RFC 4918 § 14.23, word for word: « Specifying the removal of a property that does not
        // exist is not an error ». We store five properties; removing a sixth removes nothing,
        // which is precisely what the RFC asks us to answer 200 to — without landing among the
        // five values a store write would ever see.
        var update = CalendarPropertyUpdate.Parse(Remove(new XElement(Dead("details"))));

        Assert.Empty(update.Refused);
        Assert.DoesNotContain(Dead("details"), update.Accepted.Keys);
        Assert.Contains(Dead("details"), update.RemovedAndAbsent);
    }

    [Theory]
    [InlineData("DAV:", "resourcetype")]
    [InlineData("DAV:", "getetag")]
    [InlineData("urn:ietf:params:xml:ns:caldav", "supported-calendar-component-set")]
    public void RemovingAProtectedProperty_IsStillRefused(string ns, string localName)
    {
        // RFC 4918 § 15 makes these MUST NOT be modified by a client — never absent, so § 14.23
        // does not apply to them.
        var name = XNamespace.Get(ns) + localName;

        var update = CalendarPropertyUpdate.Parse(Remove(new XElement(name)));

        Assert.Empty(update.Accepted);
        Assert.Empty(update.RemovedAndAbsent);
        Assert.Equal([name], update.Refused);
    }

    private static XName Dead(string localName) => XNamespace.Get("http://example.com/ns/") + localName;

    private static XDocument Set(params XElement[] properties) =>
        Document(DavXml.Dav + "set", properties);

    private static XDocument Remove(params XElement[] properties) =>
        Document(DavXml.Dav + "remove", properties);

    private static XDocument Document(XName instruction, XElement[] properties) =>
        new(new XElement(DavXml.Dav + "propertyupdate",
            new XElement(instruction, new XElement(DavXml.Prop, properties))));
}
