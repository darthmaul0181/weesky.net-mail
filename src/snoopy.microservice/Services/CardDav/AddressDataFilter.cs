using System.Xml.Linq;
using weesky.Snoopy.Microservice.Services.Contacts;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// Reads a <c>CARDDAV:address-data</c> element and restricts a card to what it asked for. Serving
/// the whole card to a client that asked for a subset is not a harmless surplus: the client files
/// a complete card in a cache it believes partial, and writes it back that way.
/// </summary>
internal static class AddressDataFilter
{
    // Always kept, whatever was asked: without them what comes out is not a card.
    private static readonly HashSet<string> Mandatory =
        new(["BEGIN", "END", "VERSION", "UID"], StringComparer.OrdinalIgnoreCase);

    // What supported-address-data announces. Announcing one set and accepting another is the same
    // lie in the other direction.
    private static readonly string[] Versions = ["3.0", "4.0"];

    private const string VCardMediaType = "text/vcard";

    /// <summary>
    /// Parses the element, or throws <see cref="DavPreconditionException"/>
    /// (<c>supported-address-data</c>) on a version or a content-type outside what we announce.
    /// </summary>
    internal static AddressDataRequest Parse(XElement addressData)
    {
        var version = Attribute(addressData, "version");
        if (version is not null && !Versions.Contains(version, StringComparer.Ordinal))
            throw Refused();

        var contentType = Attribute(addressData, "content-type");
        if (contentType is not null && !MediaTypeOf(contentType)
                .Equals(VCardMediaType, StringComparison.OrdinalIgnoreCase))
            throw Refused();

        var names = addressData.Elements(DavXml.CardDav + "prop")
            .Select(prop => Attribute(prop, "name"))
            .OfType<string>()
            .Where(name => name.Length > 0)
            .ToArray();
        return new AddressDataRequest(version, names);
    }

    /// <summary>
    /// The card restricted to the requested property names. BEGIN, END, VERSION and UID always
    /// survive. An empty list is the whole card, returned as stored, byte for byte.
    /// </summary>
    internal static string Restrict(string card, IReadOnlyList<string> propertyNames)
    {
        if (propertyNames.Count == 0) return card;

        var wanted = new HashSet<string>(propertyNames, StringComparer.OrdinalIgnoreCase);
        // Folded lines are rejoined first: a continuation carries no name of its own, and judging
        // it alone would truncate the value it continues.
        var kept = VCardComposer.LogicalLines(Normalized(card))
            .Where(line => Keeps(wanted, VCardComposer.NameOf(VCardComposer.Unfold(line))))
            .ToList();
        return kept.Count == 0 ? string.Empty : string.Join("\r\n", kept) + "\r\n";
    }

    private static bool Keeps(HashSet<string> wanted, string name) =>
        name.Length > 0 && (Mandatory.Contains(name) || wanted.Contains(name));

    // A verbatim .vcf may arrive with bare LF, which no split on CRLF would ever see as a line.
    private static string Normalized(string card) =>
        card.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

    private static string MediaTypeOf(string contentType)
    {
        var end = contentType.IndexOf(';');
        return (end < 0 ? contentType : contentType[..end]).Trim();
    }

    // An attribute is its local name in no namespace — the unprefixed form RFC 6352's own grammar
    // spells; a prefix bound to the CardDAV namespace names the very same attribute.
    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(a => !a.IsNamespaceDeclaration
            && a.Name.LocalName == name
            && (a.Name.Namespace == XNamespace.None || a.Name.Namespace == DavXml.CardDav))?.Value;

    private static DavPreconditionException Refused() =>
        new(DavXml.CardDav + "supported-address-data");
}
