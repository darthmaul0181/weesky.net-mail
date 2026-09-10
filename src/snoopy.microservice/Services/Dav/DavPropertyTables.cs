using System.Globalization;
using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>
/// What every property table is built from, whichever protocol owns it: the closed set of one
/// shape, its resolution against a request, and the factories the shapes of both trees share.
/// </summary>
internal static class DavPropertyTables
{
    // Trap 4. Once served, this set must ALWAYS carry write and write-content. DAVx5 only asks for
    // it in CalDAV and Thunderbird writes by default when the property is absent — but a set that is
    // PRESENT and INCOMPLETE puts Thunderbird in read-only mode. A collection has one owner and no
    // sharing, so there is no ACL model to consult: this is the honest statement that a user may do
    // everything on their own collection.
    private static readonly string[] Privileges =
    [
        "read", "write", "write-content", "write-properties", "bind", "unbind",
        "read-current-user-privilege-set"
    ];

    /// <summary>
    /// Both cost, and a client that wants either names it — in a <c>prop</c>, or in the
    /// <c>include</c> of its <c>allprop</c>. Everything else of the closed set is poured into an
    /// allprop even where its own RFC marks it "SHOULD NOT": a stable set makes approximate clients
    /// predictable, and that divergence is deliberate.
    /// </summary>
    internal static readonly XName[] AllPropExclusions =
        [DavXml.Dav + "sync-token", DavXml.Dav + "current-user-privilege-set"];

    /// <summary>
    /// The closed set for one resource, as elements, plus the names this resource does not carry —
    /// which the caller turns into the 404 propstat. A property we do not serve must come back
    /// there rather than be omitted: pure omission is what makes a client wait for ever for a value
    /// it believes is on its way.
    /// </summary>
    internal static (List<XElement> Found, List<XName> Missing) Resolve(
        DavPropertyRequest request, PropertySet set, DavResourceContext resource)
    {
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

    /// <summary>
    /// A collection that merely CONTAINS one shape — <c>/dav/principals/</c>,
    /// <c>/dav/addressbooks/</c>. Nothing of the sync model reaches here: no ctag, no sync-token,
    /// and an EMPTY <c>supported-report-set</c>, since REPORT is bound only so the <c>Allow</c>
    /// stays honest and answers the standard refusal. The 404 propstat a client then reads on
    /// sync-token is the answer, not an omission.
    /// </summary>
    internal static PropertySet IntermediateCollection(string displayName) => Set(
        (DavXml.Dav + "resourcetype", _ => new XElement(DavXml.Dav + "resourcetype",
            new XElement(DavXml.Dav + "collection"))),
        (DavXml.Dav + "displayname", _ => new XElement(DavXml.Dav + "displayname", displayName)),
        (DavXml.Dav + "current-user-principal", CurrentUserPrincipal),
        (DavXml.Dav + "principal-collection-set",
            _ => Href(DavXml.Dav + "principal-collection-set", DavPaths.PrincipalCollection)),
        (DavXml.Dav + "supported-report-set", _ => ReportSet()));

    internal static XElement CurrentUserPrincipal(DavResourceContext r) =>
        Href(DavXml.Dav + "current-user-principal", DavPaths.Principal(r.UserId));

    internal static XElement PrincipalUrl(DavResourceContext r) =>
        Href(DavXml.Dav + "principal-URL", DavPaths.Principal(r.UserId));

    /// <summary>An absolute path, never a full URL: the service sits behind a reverse proxy.</summary>
    internal static XElement Href(XName name, string path) => new(name, new XElement(DavXml.Href, path));

    internal static XElement PrivilegeSet() => new(DavXml.Dav + "current-user-privilege-set",
        Privileges.Select(p => new XElement(DavXml.Dav + "privilege", new XElement(DavXml.Dav + p))));

    internal static XElement ReportSet(params XName[] reports) =>
        new(DavXml.Dav + "supported-report-set",
            reports.Select(report => new XElement(DavXml.Dav + "supported-report",
                new XElement(DavXml.Dav + "report", new XElement(report)))));

    /// <summary>
    /// The quoted entity tag from a bare hash — what a PUT answers before any member exists, and
    /// what the <c>ETag</c> header of a GET shares with <c>getetag</c>: written twice, a conditional
    /// request would file a value no property ever advertised.
    /// </summary>
    internal static string EntityTag(string hash) => $"\"{hash}\"";

    /// <summary>
    /// "R" appends "GMT" whatever the kind carries, so the conversion has to happen first; an
    /// unspecified stamp is read as UTC, which is what the store writes. Internal because the
    /// <c>Last-Modified</c> header of a GET must come from the same source as getlastmodified.
    /// </summary>
    internal static string HttpDate(DateTime value) => (value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    }).ToString("R", CultureInfo.InvariantCulture);

    internal static PropertySet Set(
        params (XName Name, Func<DavResourceContext, XElement?> Factory)[] entries) =>
        new([.. entries.Select(e => e.Name)], entries.ToDictionary(e => e.Name, e => e.Factory));

    /// <summary>
    /// The two shared exclusions plus this table's own — the event's <c>calendar-data</c>, which
    /// RFC 4791 § 9.6 reserves to a client naming it, an allprop over a whole collection carrying
    /// every file of it otherwise.
    /// </summary>
    internal static PropertySet Excluding(PropertySet set, params XName[] more) =>
        set with { Excluded = [.. AllPropExclusions, .. more] };

    private static IEnumerable<XName> Asked(DavPropertyRequest request, PropertySet set)
    {
        if (request.Mode is not DavPropertyMode.AllProp) return request.Names;

        var excluded = set.Excluded ?? AllPropExclusions;
        var poured = set.Names.Where(name => !excluded.Contains(name)).ToList();
        return poured.Concat(request.Names.Where(name => !poured.Contains(name)));
    }

    /// <summary>
    /// The names in the order allprop and propname pour them, and the factories keyed for a named
    /// request. Two views of one array rather than a dictionary alone: a dictionary's enumeration
    /// order is an implementation detail, and a response whose property order drifts between builds
    /// is a diff nobody can read. <c>Excluded</c> is what this table's allprop leaves out, or null
    /// for the shared two.
    /// </summary>
    internal sealed record PropertySet(
        IReadOnlyList<XName> Names,
        IReadOnlyDictionary<XName, Func<DavResourceContext, XElement?>> Factories,
        IReadOnlyList<XName>? Excluded = null);
}
