using System.Globalization;
using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// A report's <c>limit/nresults</c>, read in the namespace its own RFC spells it in — RFC 6578
/// § 3.6 puts sync-collection's in <c>DAV:</c>, RFC 6352 § 10.6 addressbook-query's in CardDAV —
/// so a client's stray element in the other namespace bounds nothing. Null when absent; a value
/// that is not a positive integer is a 400.
/// </summary>
internal static class DavLimit
{
    internal static int? Read(XElement root, XNamespace ns)
    {
        var nresults = root.Element(ns + "limit")?.Element(ns + "nresults");
        if (nresults is null) return null;

        return int.TryParse(nresults.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture,
            out var bound) && bound > 0
            ? bound
            : throw new DavBadRequestException($"The {ns}limit carries no readable nresults.");
    }
}
