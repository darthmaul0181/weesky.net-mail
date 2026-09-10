using System.Xml.Linq;
using weesky.Snoopy.Microservice.Services.CalDav;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CalDav;

/// <summary>
/// What the two creation verbs read of a client's body (§ 11): the five writable properties, the
/// three refusals that stop a creation, and the whole of the rest ignored in silence.
/// </summary>
public sealed class MkCalendarRequestTests
{
    /// <summary>A VCALENDAR carrying nothing but zone blocks — what a client sends inside
    /// <c>calendar-timezone</c> (RFC 4791 § 5.2.2).</summary>
    internal static string Zones(params string[] tzids) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//weesky//tests//EN\r\n"
        + string.Concat(tzids.Select(tzid => Ics.FixedZone(tzid, "+0100")))
        + "END:VCALENDAR\r\n";

    [Fact]
    public void TheBodyDavx5Sends_IsAcceptedWhole()
    {
        // Verbatim from the DAVx5 discussion #2209: resourcetype, displayname, an EMPTY
        // calendar-description, Apple's colour, and VEVENT as the only component.
        var body = MkCalendar(
            new XElement(DavXml.Dav + "resourcetype",
                new XElement(DavXml.Dav + "collection"), new XElement(DavXml.CalDav + "calendar")),
            new XElement(DavXml.Dav + "displayname", "Perso"),
            new XElement(DavXml.CalDav + "calendar-description", string.Empty),
            new XElement(DavXml.Apple + "calendar-color", "#FF0000FF"),
            new XElement(DavXml.CalDav + "supported-calendar-component-set",
                new XElement(DavXml.CalDav + "comp", new XAttribute("name", "VEVENT"))));

        var request = MkCalendarRequest.Parse(body, extendedMkcol: false);

        Assert.Equal("Perso", request.DisplayName);
        Assert.Equal(string.Empty, request.Description);
        // Apple's alpha channel dropped and the digits folded, by the store's own judge: written
        // twice, one colour would have two spellings.
        Assert.Equal("#ff0000", request.Color);
        Assert.False(request.AsksUnsupportedComponent);
        Assert.False(request.ResourceTypeRefused);
        Assert.False(request.TimeZoneRefused);
    }

    [Fact]
    public void AppleExtrasWeDoNotModel_AreIgnoredAndNotRefused()
    {
        var body = MkCalendar(
            new XElement(DavXml.Dav + "displayname", "Perso"),
            new XElement(DavXml.CalDav + "calendar-free-busy-set",
                new XElement(DavXml.Dav + "href", "/dav/calendars/x/")),
            new XElement(DavXml.Apple + "calendar-order", "4"));

        var request = MkCalendarRequest.Parse(body, extendedMkcol: false);

        // A client that sends a property we do not keep wants a calendar, not an argument.
        Assert.Equal(4, request.Order);
        Assert.False(request.ResourceTypeRefused);
    }

    [Fact]
    public void AComponentSetAskingForVtodo_StopsTheCreation()
    {
        var body = MkCalendar(new XElement(DavXml.CalDav + "supported-calendar-component-set",
            new XElement(DavXml.CalDav + "comp", new XAttribute("name", "VEVENT")),
            new XElement(DavXml.CalDav + "comp", new XAttribute("name", "VTODO"))));

        Assert.True(MkCalendarRequest.Parse(body, extendedMkcol: false).AsksUnsupportedComponent);
    }

    [Fact]
    public void ICalsSubscribedResourceType_IsARefusalAndNotASubscription()
    {
        var body = MkCalendar(new XElement(DavXml.Dav + "resourcetype",
            new XElement(DavXml.Dav + "collection"), new XElement(DavXml.CalDav + "calendar"),
            new XElement(DavXml.CalendarServer + "subscribed")));

        Assert.True(MkCalendarRequest.Parse(body, extendedMkcol: false).ResourceTypeRefused);
    }

    [Fact]
    public void AMkcalendarWithNoResourceTypeAtAll_IsAccepted()
    {
        // Apple writes none; DAVx5 writes one. Optional on this verb, mandatory on MKCOL.
        Assert.False(MkCalendarRequest
            .Parse(MkCalendar(new XElement(DavXml.Dav + "displayname", "Perso")),
                extendedMkcol: false)
            .ResourceTypeRefused);
    }

    [Fact]
    public void AMkcalendarWithNoBodyAtAll_AsksNothingAndRefusesNothing()
    {
        var request = MkCalendarRequest.Parse(null, extendedMkcol: false);

        Assert.Null(request.DisplayName);
        Assert.False(request.ResourceTypeRefused);
        Assert.False(request.TimeZoneRefused);
        Assert.False(request.AsksUnsupportedComponent);
    }

    [Fact]
    public void AMkcolWithNoBody_DeclaresNoResourceTypeAndIsRefused()
    {
        // RFC 5689 § 3: the extended MKCOL says what it is creating, or it is creating nothing
        // this tree knows how to hold.
        Assert.True(MkCalendarRequest.Parse(null, extendedMkcol: true).ResourceTypeRefused);
    }

    [Fact]
    public void AMkcolDeclaringAPlainCollection_IsRefused()
    {
        var body = MkCol(new XElement(DavXml.Dav + "resourcetype",
            new XElement(DavXml.Dav + "collection")));

        Assert.True(MkCalendarRequest.Parse(body, extendedMkcol: true).ResourceTypeRefused);
    }

    [Fact]
    public void AMkcolDeclaringACalendar_IsAccepted()
    {
        var body = MkCol(
            new XElement(DavXml.Dav + "resourcetype",
                new XElement(DavXml.Dav + "collection"), new XElement(DavXml.CalDav + "calendar")),
            new XElement(DavXml.Dav + "displayname", "Perso"));

        var request = MkCalendarRequest.Parse(body, extendedMkcol: true);

        Assert.False(request.ResourceTypeRefused);
        Assert.Equal("Perso", request.DisplayName);
    }

    [Fact]
    public void AWindowsTzid_IsResolvedThroughTheCldrMapping()
    {
        var body = MkCalendar(new XElement(DavXml.CalDav + "calendar-timezone",
            Zones("Romance Standard Time")));

        var request = MkCalendarRequest.Parse(body, extendedMkcol: false);

        // Outlook writes Windows names; the collection stores an IANA id or nothing at all.
        Assert.Equal("Europe/Paris", request.TimeZoneId);
        Assert.False(request.TimeZoneRefused);
    }

    [Fact]
    public void AZoneNeitherTzdbNorTheMappingKnows_IsRefused()
    {
        var body = MkCalendar(new XElement(DavXml.CalDav + "calendar-timezone",
            Zones("Mars/Olympus")));

        Assert.True(MkCalendarRequest.Parse(body, extendedMkcol: false).TimeZoneRefused);
    }

    [Fact]
    public void AValueThatIsNoCalendarAtAll_IsRefused()
    {
        var body = MkCalendar(new XElement(DavXml.CalDav + "calendar-timezone", "Europe/Brussels"));

        // The property carries a VCALENDAR, not a bare id: a client sending the id alone is told
        // so rather than having it guessed at.
        Assert.True(MkCalendarRequest.Parse(body, extendedMkcol: false).TimeZoneRefused);
    }

    [Fact]
    public void AValueCarryingTwoZones_NamesNoneOfThem()
    {
        // The property is singular (RFC 4791 § 5.2.2): picking one of two would store the wrong
        // one in silence.
        var body = MkCalendar(new XElement(DavXml.CalDav + "calendar-timezone",
            Zones("Europe/Brussels", "Europe/Paris")));

        Assert.True(MkCalendarRequest.Parse(body, extendedMkcol: false).TimeZoneRefused);
    }

    [Fact]
    public void ANonsenseColourOrRank_IsSimplyNotAsked()
    {
        var body = MkCalendar(
            new XElement(DavXml.Apple + "calendar-color", "rebeccapurple"),
            new XElement(DavXml.Apple + "calendar-order", "third"));

        var request = MkCalendarRequest.Parse(body, extendedMkcol: false);

        // The palette's next colour and the last rank, exactly as if the client had sent neither:
        // § 11 gives a creation no per-property refusal but the component set's.
        Assert.Null(request.Color);
        Assert.Null(request.Order);
    }

    [Fact]
    public void ADisplayNameOfNothingButSpaces_IsNoName()
    {
        var body = MkCalendar(new XElement(DavXml.Dav + "displayname", "   "));

        Assert.Null(MkCalendarRequest.Parse(body, extendedMkcol: false).DisplayName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABodyThatIsTheOtherVerbsDocument_IsRefusedAsABadRequest(bool extendedMkcol)
    {
        var body = extendedMkcol ? MkCalendar() : MkCol();

        Assert.Throws<DavBadRequestException>(() => MkCalendarRequest.Parse(body, extendedMkcol));
    }

    private static XDocument MkCalendar(params XElement[] properties) =>
        Document(DavXml.CalDav + "mkcalendar", properties);

    private static XDocument MkCol(params XElement[] properties) =>
        Document(DavXml.Dav + "mkcol", properties);

    private static XDocument Document(XName root, XElement[] properties) =>
        new(new XElement(root,
            new XElement(DavXml.Dav + "set", new XElement(DavXml.Prop, properties))));
}
