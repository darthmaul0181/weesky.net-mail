using System.Xml.Linq;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>
/// What a MKCALENDAR or an extended MKCOL body asks of the collection being born (§ 11). The five
/// writable properties are read and everything else is IGNORED rather than refused — a client that
/// sends <c>calendar-free-busy-set</c> wants a calendar, not an argument — with four exceptions,
/// each of which stops the creation.
/// </summary>
/// <param name="DisplayName">the label asked, or null when the client sent none</param>
/// <param name="Description">as sent, empty string included</param>
/// <param name="Color">already folded to the shape the store holds, or null</param>
/// <param name="Order">the rank asked, or null</param>
/// <param name="TimeZoneId">the IANA id the VTIMEZONE resolved to, or null</param>
/// <param name="AsksUnsupportedComponent">
/// <c>supported-calendar-component-set</c> named something other than VEVENT: a 207 refusing that
/// one property, and nothing created.
/// </param>
/// <param name="ResourceTypeRefused">
/// The declared <c>resourcetype</c> is not a calendar collection, or the extended MKCOL declared
/// none at all — <c>DAV:valid-resourcetype</c>.
/// </param>
/// <param name="TimeZoneRefused">
/// <c>calendar-timezone</c> carried no single resolvable VTIMEZONE — <c>CALDAV:valid-calendar-data</c>.
/// </param>
/// <param name="Refused">
/// The protected properties the body sets, which stop the creation whole — everything else it does
/// not know is IGNORED, a client sending <c>calendar-free-busy-set</c> wanting a calendar and not
/// an argument.
/// </param>
/// <param name="NamedWritable">
/// The writable properties the body itself asked for — what an atomic refusal's propstat may name
/// as failed in dependency, and nothing beyond what the client actually sent.
/// </param>
internal sealed record MkCalendarRequest(
    string? DisplayName, string? Description, string? Color, int? Order, string? TimeZoneId,
    bool AsksUnsupportedComponent, bool ResourceTypeRefused, bool TimeZoneRefused,
    IReadOnlyList<XName> Refused, IReadOnlyList<XName> NamedWritable)
{
    private static readonly XName MkCalendar = DavXml.CalDav + "mkcalendar";
    private static readonly XName MkCol = DavXml.Dav + "mkcol";

    /// <summary>The properties RFC 4918 § 15 declares protected: a body that sets one is a body
    /// § 9.2 must refuse whole, since § 5.3.1 of RFC 4791 makes this body a PROPPATCH.</summary>
    private static readonly XName[] Protected =
    [
        DavXml.Dav + "getetag", DavXml.Dav + "getcontentlength", DavXml.Dav + "getlastmodified",
        DavXml.Dav + "getcontenttype", DavXml.Dav + "creationdate", DavXml.Dav + "lockdiscovery",
        DavXml.Dav + "supportedlock",
    ];

    /// <param name="body">the parsed body, or null when the request carried none</param>
    /// <param name="extendedMkcol">
    /// True for MKCOL, whose body is mandatory and must declare a calendar (RFC 5689 § 3); a
    /// MKCALENDAR may carry none at all, which is the bare creation Apple's clients send.
    /// </param>
    /// <exception cref="DavBadRequestException">the body is not the verb's own document</exception>
    internal static MkCalendarRequest Parse(XDocument? body, bool extendedMkcol)
    {
        if (body is null)
            return new MkCalendarRequest(null, null, null, null, null, false, extendedMkcol, false, [], []);

        var root = extendedMkcol ? MkCol : MkCalendar;
        if (body.Root?.Name != root)
        {
            throw new DavBadRequestException(
                $"A {(extendedMkcol ? "MKCOL" : "MKCALENDAR")} body must be a {root} document.");
        }

        var asked = body.Root.Elements(DavXml.Dav + "set")
            .SelectMany(set => set.Elements(DavXml.Prop))
            .SelectMany(prop => prop.Elements())
            .ToList();

        XElement? First(XName name) => asked.FirstOrDefault(property => property.Name == name);

        var resourceType = First(CalendarPropertyValue.ResourceType);
        var zone = First(CalendarPropertyValue.TimeZone);
        var components = First(CalendarPropertyValue.ComponentSet);
        var resolved = zone is null ? null : CalendarPropertyValue.Zone(zone.Value);
        var askedNames = asked.Select(property => property.Name).Distinct().ToList();
        var refused = askedNames.Where(Protected.Contains).ToList();
        var namedWritable = askedNames.Where(CalendarPropertyValue.Writable.Contains).ToList();

        return new MkCalendarRequest(
            First(CalendarPropertyValue.DisplayName) is { } name
                ? CalendarPropertyValue.Name(name.Value)
                : null,
            First(CalendarPropertyValue.Description)?.Value,
            First(CalendarPropertyValue.Color) is { } colour
                ? CalendarPropertyValue.Colour(colour.Value)
                : null,
            First(CalendarPropertyValue.Order) is { } order
                ? CalendarPropertyValue.Rank(order.Value)
                : null,
            resolved,
            components is not null && !CalendarPropertyValue.OnlyEvents(components),
            // Optional on MKCALENDAR (DAVx5 writes it, Apple does not), mandatory on MKCOL, and
            // wrong in both when it says anything but a calendar collection.
            resourceType is null
                ? extendedMkcol
                : !CalendarPropertyValue.IsCalendar(resourceType),
            zone is not null && resolved is null,
            refused, namedWritable);
    }
}
