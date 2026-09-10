using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>
/// What one protocol accepts in a <c>collation</c> attribute, what it answers when the attribute
/// is absent, and the condition it refuses the rest with — exactly what its
/// <c>supported-collation-set</c> announces, so a collation accepted here is one that was
/// advertised. CardDAV (RFC 6352 § 8.3.1): ascii + unicode, unicode by default. CalDAV
/// (RFC 4791 § 7.5.1): ascii + octet, ascii by default.
/// </summary>
internal sealed record DavCollationSet(
    IReadOnlyDictionary<string, DavCollationComparer> Accepted, DavCollationComparer Default, XName Refusal)
{
    internal static readonly DavCollationSet CardDav = new(
        Named((DavCollation.AsciiCasemap, DavCollation.Ascii), (DavCollation.UnicodeCasemap, DavCollation.Unicode)),
        DavCollation.Unicode, DavXml.CardDav + "supported-collation");

    internal static readonly DavCollationSet CalDav = new(
        Named((DavCollation.AsciiCasemap, DavCollation.Ascii), (DavCollation.Octet, DavCollation.Ordinal)),
        DavCollation.Ascii, DavXml.CalDav + "supported-collation");

    private static Dictionary<string, DavCollationComparer> Named(
        params (string Name, DavCollationComparer Comparer)[] collations) =>
        collations.ToDictionary(c => c.Name, c => c.Comparer, StringComparer.OrdinalIgnoreCase);
}
