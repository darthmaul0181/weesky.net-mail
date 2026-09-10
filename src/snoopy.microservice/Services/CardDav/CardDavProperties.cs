using System.Globalization;
using System.Text;
using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Dav;
using static weesky.Snoopy.Microservice.Services.Dav.DavPropertyTables;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// The closed property set the server serves on the address-book tree, one table per
/// <see cref="DavResourceKind"/>. A client does not ask for the properties a server finds
/// interesting; it asks for the ones its screen needs and reads an absence as a broken book — so
/// the list is written here once, rather than discovered slice by slice through bug reports.
/// </summary>
internal static class CardDavProperties
{
    private const string CardContentType = "text/vcard; charset=utf-8";

    /// <summary>What the user reads in their client for the home and for the book itself.</summary>
    private const string HomeDisplayName = "Address Books";

    private const string CollectionDisplayName = "Contacts";

    /// <summary>The two RFC 6352 § 8.3 makes mandatory, and no more.</summary>
    private static readonly string[] Collations =
        [DavCollation.AsciiCasemap, DavCollation.UnicodeCasemap];

    private static readonly Dictionary<DavResourceKind, PropertySet> Tables = new()
    {
        [DavResourceKind.AddressBookCollection] = IntermediateCollection("Address Book Homes"),

        [DavResourceKind.AddressBookHome] = Set(
            (DavXml.Dav + "resourcetype", _ => new XElement(DavXml.Dav + "resourcetype",
                new XElement(DavXml.Dav + "collection"))),
            (DavXml.Dav + "displayname", _ => new XElement(DavXml.Dav + "displayname", HomeDisplayName)),
            // Same rule as the service root's: the home's Allow names REPORT and expand-property is
            // what it serves, so the property has to say so rather than come back in a 404 propstat.
            (DavXml.Dav + "supported-report-set", _ => ReportSet(DavXml.Dav + "expand-property")),
            (DavXml.Dav + "current-user-principal", CurrentUserPrincipal)),

        [DavResourceKind.AddressBook] = Set(
            (DavXml.Dav + "resourcetype", _ => new XElement(DavXml.Dav + "resourcetype",
                new XElement(DavXml.Dav + "collection"), new XElement(DavXml.CardDav + "addressbook"))),
            (DavXml.Dav + "displayname",
                _ => new XElement(DavXml.Dav + "displayname", CollectionDisplayName)),
            // Trap 6. getctag is an extension, not a RFC — hence the CalendarServer namespace. It is
            // served anyway because DAVx5 asks for it on every status poll and falls back to it when
            // sync-collection is unavailable.
            (DavXml.CalendarServer + "getctag", r => new XElement(DavXml.CalendarServer + "getctag",
                DavSyncToken.Ctag(r.State))),
            (DavXml.Dav + "sync-token",
                r => new XElement(DavXml.Dav + "sync-token", DavSyncToken.Token(r.State))),
            // What this build ANSWERS, not what the slice will. Announcing a report REPORT
            // refuses made DAVx5 loop: ctag poll, sync-collection, 403, start over — observed on
            // Android, never falling back to the Depth: 1 listing, so the book never filled.
            // sync-collection and addressbook-query are both here because REPORT now serves them;
            // a report announced and refused is what made the client loop in the first place.
            (DavXml.Dav + "supported-report-set", _ => ReportSet(
                DavXml.CardDav + "addressbook-multiget", DavXml.CardDav + "addressbook-query",
                DavXml.Dav + "expand-property", DavXml.Dav + "sync-collection")),
            // The book stores both versions verbatim and serves what it holds; announcing 3.0 alone
            // would make half the answers a lie.
            (DavXml.CardDav + "supported-address-data", _ => new XElement(
                DavXml.CardDav + "supported-address-data",
                AddressDataType("3.0"), AddressDataType("4.0"))),
            (DavXml.CardDav + "supported-collation-set", _ => new XElement(
                DavXml.CardDav + "supported-collation-set",
                Collations.Select(c => new XElement(DavXml.CardDav + "supported-collation", c)))),
            // Trap 3. The store's own constant, never a literal recopied here: an announced value the
            // store would violate, or the reverse, is paid for in cards refused without the client
            // understanding why.
            (DavXml.CardDav + "max-resource-size", _ => new XElement(
                DavXml.CardDav + "max-resource-size",
                ContactStore.MaxCardBytes.ToString(CultureInfo.InvariantCulture))),
            (DavXml.Dav + "current-user-principal", CurrentUserPrincipal),
            (DavXml.Dav + "current-user-privilege-set", _ => PrivilegeSet()),
            (DavXml.Dav + "owner", r => Href(DavXml.Dav + "owner", DavPaths.Principal(r.UserId)))),

        [DavResourceKind.Card] = Set(
            (DavXml.Dav + "getetag", r => FromCard(r, DavXml.Dav + "getetag", EntityTag)),
            (DavXml.Dav + "getcontenttype",
                r => FromCard(r, DavXml.Dav + "getcontenttype", _ => CardContentType)),
            // Trap 1. A count of UTF-8 BYTES, never of characters — the unit ContactStore.MaxCardBytes
            // and max-resource-size already use. An accented card would otherwise announce a length
            // below its body, and a client that cuts at the announced length receives a truncated
            // card: invalid, rejected, with nothing to say why.
            (DavXml.Dav + "getcontentlength", r => FromCard(r, DavXml.Dav + "getcontentlength",
                c => Encoding.UTF8.GetByteCount(c.VCardRaw).ToString(CultureInfo.InvariantCulture))),
            // Trap 2. HTTP-date in GMT, never ISO, which nothing reads here. It comes from
            // contacts.updated_at, which also moves on a favourite being toggled — a named breach of
            // decision 6's invisibility, left as it is: no client synchronises on getlastmodified,
            // they all follow the ETag and the sequence, and neither moves.
            (DavXml.Dav + "getlastmodified",
                r => FromCard(r, DavXml.Dav + "getlastmodified", c => HttpDate(c.UpdatedAt))),
            (DavXml.Dav + "resourcetype", _ => new XElement(DavXml.Dav + "resourcetype")),
            (DavXml.Dav + "current-user-privilege-set", _ => PrivilegeSet()),
            // Both, because REPORT answers both on a card: § 8.6 and § 8.7 each define their
            // report on an address resource, and a Depth: 0 query on a card is sabre's nominal
            // case for that Depth.
            (DavXml.Dav + "supported-report-set", _ => ReportSet(
                DavXml.CardDav + "addressbook-multiget", DavXml.CardDav + "addressbook-query"))),
    };

    /// <summary>
    /// The book's own shapes, and above them the principal's: an expand-property on the book nests
    /// into <c>owner</c> and <c>current-user-principal</c>, so the tables it resolves against must
    /// reach the shapes those hrefs designate.
    /// </summary>
    internal static (List<XElement> Found, List<XName> Missing) Resolve(
        DavPropertyRequest request, DavResourceContext resource) =>
        Tables.TryGetValue(resource.Kind, out var set)
            ? DavPropertyTables.Resolve(request, set, resource)
            : DavPrincipalProperties.Resolve(request, resource);

    /// <summary>The quoted entity tag, shared with the <c>ETag</c> header a GET answers.</summary>
    internal static string EntityTag(DavCard card) => DavPropertyTables.EntityTag(card.CardHash);

    private static XElement AddressDataType(string version) =>
        new(DavXml.CardDav + "address-data-type",
            new XAttribute("content-type", "text/vcard"), new XAttribute("version", version));

    private static XElement? FromCard(DavResourceContext resource, XName name, Func<DavCard, string> value) =>
        resource.Card is { } card ? new XElement(name, value(card)) : null;
}
