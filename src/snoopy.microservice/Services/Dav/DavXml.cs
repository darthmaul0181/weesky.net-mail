using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>
/// The protocol's namespaces and element names, every one as an <see cref="XName"/> rather than a
/// prefixed string: a client writes <c>D:</c>, <c>d:</c>, <c>a:</c> or no prefix at all, binding
/// <c>DAV:</c> to whatever it likes, and a reader comparing a string like <c>"D:prop"</c> works
/// against the RFC's own examples and fails against the first real one.
/// </summary>
internal static class DavXml
{
    internal static readonly XNamespace Dav = "DAV:";
    internal static readonly XNamespace CardDav = "urn:ietf:params:xml:ns:carddav";
    internal static readonly XNamespace CalDav = "urn:ietf:params:xml:ns:caldav";

    /// <summary>Apple's calendar extensions — <c>calendar-color</c> and <c>calendar-order</c>,
    /// which iOS and Thunderbird both read and write; no RFC defines either.</summary>
    internal static readonly XNamespace Apple = "http://apple.com/ns/ical/";

    /// <summary>
    /// getctag is an extension, not a RFC: no RFC of this slice defines it, and it is served
    /// anyway because DAVx5 asks for it on every status poll and falls back to it when
    /// sync-collection is unavailable.
    /// </summary>
    internal static readonly XNamespace CalendarServer = "http://calendarserver.org/ns/";

    internal static readonly XName Prop = Dav + "prop";
    internal static readonly XName PropStat = Dav + "propstat";
    internal static readonly XName Response = Dav + "response";
    internal static readonly XName MultiStatus = Dav + "multistatus";
    internal static readonly XName Href = Dav + "href";
    internal static readonly XName Status = Dav + "status";
    internal static readonly XName Error = Dav + "error";

    /// <summary>
    /// An attribute is its local name in no namespace — the unprefixed form RFC 6352's own grammar
    /// spells; a prefix bound to the CardDAV namespace names the very same attribute.
    /// </summary>
    internal static string? Attribute(XElement element, string name) => Attribute(element, name, CardDav);

    /// <summary>The same reading for the other protocol, whose grammar (RFC 4791 § 9.7) spells its
    /// attributes unprefixed too: a prefix bound to <paramref name="protocol"/> names the same one.</summary>
    internal static string? Attribute(XElement element, string name, XNamespace protocol) =>
        element.Attributes().FirstOrDefault(a => !a.IsNamespaceDeclaration
            && a.Name.LocalName == name
            && (a.Name.Namespace == XNamespace.None || a.Name.Namespace == protocol))?.Value;
}
