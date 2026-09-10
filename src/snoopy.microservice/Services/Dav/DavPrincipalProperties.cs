using System.Xml.Linq;
using static weesky.Snoopy.Microservice.Services.Dav.DavPropertyTables;

namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>
/// The tables of the shapes both protocols share — the service root, the collection of principals
/// and the principal itself, where discovery starts and where the home-sets are announced.
/// </summary>
internal static class DavPrincipalProperties
{
    /// <summary>The one property of this table whose value COSTS — a call to the platform — so the
    /// controller reads this name to decide whether to gather it at all.</summary>
    internal static readonly XName CalendarUserAddressSet = DavXml.CalDav + "calendar-user-address-set";

    private static readonly Dictionary<DavResourceKind, PropertySet> Tables = new()
    {
        [DavResourceKind.ServiceRoot] = Set(
            (DavXml.Dav + "current-user-principal", CurrentUserPrincipal),
            (DavXml.Dav + "principal-URL", PrincipalUrl),
            // RFC 3253 § 3.1.5 makes this a live property of every resource serving REPORT, and
            // this one does: its Allow names the verb and expand-property genuinely resolves here.
            // Absent, a client asking for it reads a 404 propstat on the shape it opens discovery
            // on — the same Allow-says-one-thing, answer-says-another that made DAVx5 loop.
            (DavXml.Dav + "supported-report-set", _ => ReportSet(DavXml.Dav + "expand-property")),
            (DavXml.Dav + "resourcetype", _ => new XElement(DavXml.Dav + "resourcetype"))),

        [DavResourceKind.PrincipalCollection] = IntermediateCollection("Principals"),

        [DavResourceKind.Principal] = Set(
            (DavXml.Dav + "resourcetype", _ => new XElement(DavXml.Dav + "resourcetype",
                new XElement(DavXml.Dav + "principal"))),
            (DavXml.Dav + "current-user-principal", CurrentUserPrincipal),
            (DavXml.Dav + "principal-URL", PrincipalUrl),
            (DavXml.Dav + "displayname", r => new XElement(DavXml.Dav + "displayname", r.PrincipalAddress)),
            // Each home-set only where its service is on: announced anyway, the client follows the
            // href on every cycle to read the 403 a switched-off protocol answers, and files an
            // error the user never asked for. A 404 propstat says "not here", which is the truth.
            (DavXml.CardDav + "addressbook-home-set", r => r.CardDavEnabled
                ? Href(DavXml.CardDav + "addressbook-home-set", DavPaths.Home(r.UserId))
                : null),
            (DavXml.CalDav + "calendar-home-set", r => r.CalDavEnabled
                ? Href(DavXml.CalDav + "calendar-home-set", DavPaths.CalendarHome(r.UserId))
                : null),
            // RFC 6638 § 2.4.1, of an extension this build serves nothing else of: it is by this
            // property that a client recognises itself as a participant of an invitation it
            // received on an alias, and the absence of calendar-auto-schedule from the DAV: header
            // says clearly enough that no scheduling follows. Null — a 404 propstat — when the
            // controller did not gather the list, which it does only for a body that asks.
            (CalendarUserAddressSet, r => r.Addresses is { } addresses
                ? new XElement(CalendarUserAddressSet,
                    addresses.Select(address => new XElement(DavXml.Href, $"mailto:{address}")))
                : null),
            // RFC 3744 § 5.8: the collections that CONTAIN principals, not the principal itself.
            (DavXml.Dav + "principal-collection-set",
                _ => Href(DavXml.Dav + "principal-collection-set", DavPaths.PrincipalCollection)),
            // Trap 7. supported-report-set is served on the principal AND on the cards, not only on
            // the book: RFC 6352 § 8 asks for it on address resources as much as on collections.
            (DavXml.Dav + "supported-report-set", _ => ReportSet(DavXml.Dav + "expand-property")),
            // Trap 5. Both are EMPTY elements, and both are written. RFC 3744 § 4 makes them
            // mandatory on any principal; omitting them lets a client conclude the principal is not
            // one.
            (DavXml.Dav + "alternate-URI-set", _ => new XElement(DavXml.Dav + "alternate-URI-set")),
            (DavXml.Dav + "group-membership", _ => new XElement(DavXml.Dav + "group-membership"))),
    };

    /// <summary>
    /// A kind with no table here answers every asked property as a 404 propstat rather than
    /// throwing: this is where an expand-property nested onto a home-set lands, and the shapes that
    /// have a table of their own elsewhere are routed by the controller before they get here. An
    /// indexer would make the next href property added to the principal a 500.
    /// </summary>
    private static readonly PropertySet Empty = Set();

    internal static (List<XElement> Found, List<XName> Missing) Resolve(
        DavPropertyRequest request, DavResourceContext resource) =>
        DavPropertyTables.Resolve(
            request, Tables.TryGetValue(resource.Kind, out var table) ? table : Empty, resource);
}
