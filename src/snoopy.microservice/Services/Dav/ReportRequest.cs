using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>
/// Names the report a REPORT body carries. Recognition reads the root's namespace and local name
/// and nothing else — a client writes <c>C:</c>, <c>card:</c> or no prefix at all, and a reader
/// comparing a prefixed string works against the RFC's examples and fails against the first real
/// client.
/// </summary>
internal static class ReportRequest
{
    private static readonly XName Multiget = DavXml.CardDav + "addressbook-multiget";
    private static readonly XName Query = DavXml.CardDav + "addressbook-query";
    private static readonly XName SyncCollection = DavXml.Dav + "sync-collection";
    private static readonly XName ExpandProperty = DavXml.Dav + "expand-property";
    private static readonly XName CalendarMultiget = DavXml.CalDav + "calendar-multiget";
    private static readonly XName CalendarQuery = DavXml.CalDav + "calendar-query";
    private static readonly XName FreeBusyQuery = DavXml.CalDav + "free-busy-query";

    /// <summary>The report a body names, by namespace and local name of its root — never by prefix.</summary>
    internal static DavReportKind KindOf(XDocument body)
    {
        var root = body.Root?.Name;
        if (root == Multiget) return DavReportKind.Multiget;
        if (root == Query) return DavReportKind.Query;
        if (root == SyncCollection) return DavReportKind.SyncCollection;
        if (root == ExpandProperty) return DavReportKind.ExpandProperty;
        if (root == CalendarMultiget) return DavReportKind.CalendarMultiget;
        if (root == CalendarQuery) return DavReportKind.CalendarQuery;
        if (root == FreeBusyQuery) return DavReportKind.FreeBusyQuery;
        return DavReportKind.Unknown;
    }
}

/// <summary>
/// The reports this surface knows by name, on both trees: a report is named so its refusal is
/// a considered <c>403 supported-report</c> rather than a fall through, and the two multigets
/// are two names — served on the other tree, one would read the other's hrefs as its own.
/// </summary>
internal enum DavReportKind
{
    Multiget,
    Query,
    SyncCollection,
    ExpandProperty,
    CalendarMultiget,
    CalendarQuery,
    FreeBusyQuery,
    Unknown
}
