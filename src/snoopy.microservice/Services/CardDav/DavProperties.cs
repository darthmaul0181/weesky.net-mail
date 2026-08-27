using System.Globalization;
using System.Text;
using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// The closed property set the server serves, one table per <see cref="DavResourceKind"/>. A client
/// does not ask for the properties a server finds interesting; it asks for the ones its screen needs
/// and reads an absence as a broken book — so the list is written here once, rather than discovered
/// slice by slice through bug reports.
/// </summary>
internal static class DavProperties
{
    private const string CardContentType = "text/vcard; charset=utf-8";

    /// <summary>What the user reads in their client for the home and for the book itself.</summary>
    private const string HomeDisplayName = "Address Books";

    private const string CollectionDisplayName = "Contacts";

    // Trap 4. Once served, this set must ALWAYS carry write and write-content. DAVx5 only asks for
    // it in CalDAV and Thunderbird writes by default when the property is absent — but a set that is
    // PRESENT and INCOMPLETE puts Thunderbird in read-only mode. The book has one owner and no
    // sharing, so there is no ACL model to consult: this is the honest statement that a user may do
    // everything on their own book.
    private static readonly string[] Privileges =
    [
        "read", "write", "write-content", "write-properties", "bind", "unbind",
        "read-current-user-privilege-set"
    ];

    /// <summary>The two RFC 6352 § 8.3 makes mandatory, and no more.</summary>
    private static readonly string[] Collations = ["i;ascii-casemap", "i;unicode-casemap"];

    /// <summary>
    /// Both cost, and a client that wants either names it — in a <c>prop</c>, or in the
    /// <c>include</c> of its <c>allprop</c>. Everything else of the closed set is poured into an
    /// allprop even where its own RFC marks it "SHOULD NOT": a stable set makes approximate clients
    /// predictable, and that divergence is deliberate.
    /// </summary>
    private static readonly XName[] AllPropExclusions =
        [DavXml.Dav + "sync-token", DavXml.Dav + "current-user-privilege-set"];

    private static readonly Dictionary<DavResourceKind, PropertySet> Tables = new()
    {
        [DavResourceKind.ServiceRoot] = Set(
            (DavXml.Dav + "current-user-principal", CurrentUserPrincipal),
            (DavXml.Dav + "principal-URL", PrincipalUrl),
            (DavXml.Dav + "resourcetype", _ => new XElement(DavXml.Dav + "resourcetype"))),

        [DavResourceKind.Principal] = Set(
            (DavXml.Dav + "resourcetype", _ => new XElement(DavXml.Dav + "resourcetype",
                new XElement(DavXml.Dav + "principal"))),
            (DavXml.Dav + "current-user-principal", CurrentUserPrincipal),
            (DavXml.Dav + "principal-URL", PrincipalUrl),
            (DavXml.Dav + "displayname", r => new XElement(DavXml.Dav + "displayname", r.PrincipalAddress)),
            (DavXml.CardDav + "addressbook-home-set",
                r => Href(DavXml.CardDav + "addressbook-home-set", DavPaths.Home(r.UserId))),
            // The collection principals may be found in. We publish one principal per user and no
            // listing above it, so it is the principal's own URL: a client searching there finds the
            // only principal that exists.
            (DavXml.Dav + "principal-collection-set",
                r => Href(DavXml.Dav + "principal-collection-set", DavPaths.Principal(r.UserId))),
            // Trap 7. supported-report-set is served on the principal AND on the cards, not only on
            // the book: RFC 6352 § 8 asks for it on address resources as much as on collections.
            (DavXml.Dav + "supported-report-set", _ => ReportSet(DavXml.Dav + "expand-property")),
            // Trap 5. Both are EMPTY elements, and both are written. RFC 3744 § 4 makes them
            // mandatory on any principal; omitting them lets a client conclude the principal is not
            // one.
            (DavXml.Dav + "alternate-URI-set", _ => new XElement(DavXml.Dav + "alternate-URI-set")),
            (DavXml.Dav + "group-membership", _ => new XElement(DavXml.Dav + "group-membership"))),

        [DavResourceKind.Home] = Set(
            (DavXml.Dav + "resourcetype", _ => new XElement(DavXml.Dav + "resourcetype",
                new XElement(DavXml.Dav + "collection"))),
            (DavXml.Dav + "displayname", _ => new XElement(DavXml.Dav + "displayname", HomeDisplayName)),
            (DavXml.Dav + "current-user-principal", CurrentUserPrincipal)),

        [DavResourceKind.Collection] = Set(
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
            // The slice's four. Two of them are answered 403 supported-report until plan c replaces
            // the refusal with the implementation, and no /dav route is open in production before
            // then: the set is the slice's set, not today's build's.
            (DavXml.Dav + "supported-report-set", _ => ReportSet(
                DavXml.CardDav + "addressbook-query", DavXml.CardDav + "addressbook-multiget",
                DavXml.Dav + "sync-collection", DavXml.Dav + "expand-property")),
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
            (DavXml.Dav + "getetag", r => FromCard(r, DavXml.Dav + "getetag", c => $"\"{c.CardHash}\"")),
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
            (DavXml.Dav + "supported-report-set", _ => ReportSet(
                DavXml.CardDav + "addressbook-multiget", DavXml.CardDav + "addressbook-query"))),
    };

    /// <summary>
    /// The closed set for one resource, as elements, plus the names this resource does not carry —
    /// which the caller turns into the 404 propstat. A property we do not serve must come back
    /// there rather than be omitted: pure omission is what makes a client wait for ever for a value
    /// it believes is on its way.
    /// </summary>
    internal static (List<XElement> Found, List<XName> Missing) Resolve(
        DavPropertyRequest request, DavResourceContext resource)
    {
        var set = Tables[resource.Kind];
        List<XElement> found = [];
        List<XName> missing = [];

        if (request.Mode is DavPropertyMode.PropName)
        {
            found.AddRange(set.Names.Select(name => new XElement(name)));
            return (found, missing);
        }

        foreach (var name in Asked(request, set))
        {
            if (set.Factories.TryGetValue(name, out var factory) && factory(resource) is { } element)
                found.Add(element);
            else
                missing.Add(name);
        }

        return (found, missing);
    }

    private static IEnumerable<XName> Asked(DavPropertyRequest request, PropertySet set)
    {
        if (request.Mode is not DavPropertyMode.AllProp) return request.Names;

        var poured = set.Names.Where(name => !AllPropExclusions.Contains(name)).ToList();
        return poured.Concat(request.Names.Where(name => !poured.Contains(name)));
    }

    private static XElement CurrentUserPrincipal(DavResourceContext r) =>
        Href(DavXml.Dav + "current-user-principal", DavPaths.Principal(r.UserId));

    private static XElement PrincipalUrl(DavResourceContext r) =>
        Href(DavXml.Dav + "principal-URL", DavPaths.Principal(r.UserId));

    /// <summary>An absolute path, never a full URL: the service sits behind a reverse proxy.</summary>
    private static XElement Href(XName name, string path) => new(name, new XElement(DavXml.Href, path));

    private static XElement PrivilegeSet() => new(DavXml.Dav + "current-user-privilege-set",
        Privileges.Select(p => new XElement(DavXml.Dav + "privilege", new XElement(DavXml.Dav + p))));

    private static XElement ReportSet(params XName[] reports) =>
        new(DavXml.Dav + "supported-report-set",
            reports.Select(report => new XElement(DavXml.Dav + "supported-report",
                new XElement(DavXml.Dav + "report", new XElement(report)))));

    private static XElement AddressDataType(string version) =>
        new(DavXml.CardDav + "address-data-type",
            new XAttribute("content-type", "text/vcard"), new XAttribute("version", version));

    private static XElement? FromCard(DavResourceContext resource, XName name, Func<DavCard, string> value) =>
        resource.Card is { } card ? new XElement(name, value(card)) : null;

    /// <summary>
    /// "R" appends "GMT" whatever the kind carries, so the conversion has to happen first; an
    /// unspecified stamp is read as UTC, which is what the store writes.
    /// </summary>
    private static string HttpDate(DateTime value) => (value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    }).ToString("R", CultureInfo.InvariantCulture);

    private static PropertySet Set(
        params (XName Name, Func<DavResourceContext, XElement?> Factory)[] entries) =>
        new([.. entries.Select(e => e.Name)], entries.ToDictionary(e => e.Name, e => e.Factory));

    /// <summary>
    /// The names in the order allprop and propname pour them, and the factories keyed for a named
    /// request. Two views of one array rather than a dictionary alone: a dictionary's enumeration
    /// order is an implementation detail, and a response whose property order drifts between builds
    /// is a diff nobody can read.
    /// </summary>
    private sealed record PropertySet(
        IReadOnlyList<XName> Names,
        IReadOnlyDictionary<XName, Func<DavResourceContext, XElement?>> Factories);
}
