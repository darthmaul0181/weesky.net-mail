using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>
/// The property NAMES a PROPPATCH body asks to write or to erase (RFC 4918 § 9.2), split by
/// instruction: nothing here is ever stored, so a <c>DAV:set</c> stays § 9.2.1's <c>403
/// Forbidden</c> whatever it names, but a <c>DAV:remove</c> of a property this shape does not
/// carry is § 14.23's "not an error" — the caller judges that half against its own closed set,
/// this type only tells the two instructions apart.
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
    /// <returns>
    /// The names of <c>DAV:set</c>, and of <c>DAV:remove</c>, each in document order and each
    /// name once: first judgement wins, so a name in both keeps only the earlier instruction's.
    /// </returns>
    /// <exception cref="DavBadRequestException">
    /// The body is absent or is not a <c>DAV:propertyupdate</c>. § 9.2 requires one, and a request
    /// naming nothing to change is a client bug worth telling apart from one whose properties were
    /// all refused — which is what a 207 full of 403s would say instead.
    /// </exception>
    internal static (IReadOnlyList<XName> SetNames, IReadOnlyList<XName> RemoveNames) NamesIn(
        XDocument? document)
    {
        if (document?.Root is not { } root || root.Name != PropertyUpdate)
            throw new DavBadRequestException("A PROPPATCH body must be a DAV:propertyupdate document.");

        List<XName> set = [];
        List<XName> remove = [];

        foreach (var instruction in root.Elements())
        {
            if (instruction.Name != Set && instruction.Name != Remove) continue;
            var target = instruction.Name == Remove ? remove : set;

            foreach (var property in instruction.Elements(DavXml.Prop).SelectMany(prop => prop.Elements()))
            {
                if (set.Contains(property.Name) || remove.Contains(property.Name)) continue;
                target.Add(property.Name);
            }
        }

        return (set, remove);
    }
}
