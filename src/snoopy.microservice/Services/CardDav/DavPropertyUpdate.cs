using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// The property NAMES a PROPPATCH body asks to write or to erase (RFC 4918 § 9.2) — both
/// <c>DAV:set</c> and <c>DAV:remove</c>, in the order the document writes them, each name once.
/// No value is ever read, because nothing is stored: the answer to every one of these names is
/// § 9.2.1's <c>403 Forbidden</c>.
/// </summary>
internal static class DavPropertyUpdate
{
    private static readonly XName PropertyUpdate = DavXml.Dav + "propertyupdate";
    private static readonly XName Set = DavXml.Dav + "set";
    private static readonly XName Remove = DavXml.Dav + "remove";

    /// <summary>
    /// Recognises every element by namespace AND local name: a client writes <c>D:</c>, <c>A:</c>
    /// or no prefix at all, and a reader comparing the string <c>"D:set"</c> works against the
    /// RFC's own examples and fails against the first real client.
    /// </summary>
    /// <param name="document">the parsed body, or null when it was empty</param>
    /// <exception cref="DavBadRequestException">
    /// The body is absent or is not a <c>DAV:propertyupdate</c>. § 9.2 requires one, and a request
    /// naming nothing to change is a client bug worth telling apart from one whose properties were
    /// all refused — which is what a 207 full of 403s would say instead.
    /// </exception>
    internal static IReadOnlyList<XName> NamesIn(XDocument? document)
    {
        if (document?.Root is not { } root || root.Name != PropertyUpdate)
            throw new DavBadRequestException("A PROPPATCH body must be a DAV:propertyupdate document.");

        return
        [
            .. root.Elements().Where(child => child.Name == Set || child.Name == Remove)
                .SelectMany(child => child.Elements(DavXml.Prop))
                .SelectMany(prop => prop.Elements())
                .Select(property => property.Name)
                .Distinct()
        ];
    }
}
