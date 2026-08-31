using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.CardDav;

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

    /// <summary>The report a body names, by namespace and local name of its root — never by prefix.</summary>
    internal static DavReportKind KindOf(XDocument body)
    {
        var root = body.Root?.Name;
        if (root == Multiget) return DavReportKind.Multiget;
        if (root == Query) return DavReportKind.Query;
        if (root == SyncCollection) return DavReportKind.SyncCollection;
        if (root == ExpandProperty) return DavReportKind.ExpandProperty;
        return DavReportKind.Unknown;
    }
}

/// <summary>
/// The reports this surface knows by name. <see cref="Query"/> and <see cref="SyncCollection"/>
/// are named so their refusal is a considered <c>403 supported-report</c> rather than a fall
/// through — plan c replaces the refusal with an implementation and extends nothing here but the
/// controller's switch.
/// </summary>
internal enum DavReportKind
{
    Multiget,
    Query,
    SyncCollection,
    ExpandProperty,
    Unknown
}
