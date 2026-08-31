using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// What a PROPFIND body asked for. An empty body means <see cref="DavPropertyMode.AllProp"/>
/// (RFC 4918 § 9.1), and several clients send one at discovery.
/// </summary>
/// <param name="Mode">Which of the three shapes the body took.</param>
/// <param name="Names">
/// The properties named in <c>prop</c>, or the ones an <c>allprop</c> named in its sibling
/// <c>include</c>. Empty in every other case.
/// </param>
internal sealed record DavPropertyRequest(DavPropertyMode Mode, IReadOnlyList<XName> Names)
{
    private static readonly XName AllPropElement = DavXml.Dav + "allprop";
    private static readonly XName PropNameElement = DavXml.Dav + "propname";
    private static readonly XName IncludeElement = DavXml.Dav + "include";

    private static readonly DavPropertyRequest EverythingWeServe = new(DavPropertyMode.AllProp, []);

    /// <summary>
    /// Reads the root's own children — <c>prop</c> sits directly under <c>propfind</c> and under
    /// every report body of this slice alike. Each element is recognised by its namespace AND its
    /// local name: a client writes <c>D:</c>, <c>d:</c>, <c>a:</c> or no prefix at all and binds
    /// <c>DAV:</c> to whatever it likes, so a reader comparing the string <c>"D:prop"</c> works
    /// against the RFC's own examples and fails against the first real client.
    /// </summary>
    /// <param name="document">The parsed body, or null for an empty one.</param>
    internal static DavPropertyRequest Parse(XDocument? document)
    {
        if (document?.Root is not { } root) return EverythingWeServe;

        foreach (var child in root.Elements())
        {
            if (child.Name == DavXml.Prop)
                return new DavPropertyRequest(DavPropertyMode.Named, NamesIn(child));
            if (child.Name == PropNameElement)
                return new DavPropertyRequest(DavPropertyMode.PropName, []);
            if (child.Name == AllPropElement)
                // RFC 4918 § 14.8: an allprop may name extra properties in a sibling include, and
                // that is precisely how a client asks for the two allprop leaves out.
                return new DavPropertyRequest(DavPropertyMode.AllProp,
                    root.Element(IncludeElement) is { } include ? NamesIn(include) : []);
        }

        return EverythingWeServe;
    }

    /// <summary>
    /// RFC 4918 § 14.20's own grammar — <c>propfind = (propname | (allprop, include?) | prop)</c> —
    /// enforced on the PROPFIND path ALONE. <see cref="Parse"/> stays permissive because it is
    /// shared with the reports, whose roots legitimately carry <c>filter</c>, <c>limit</c>,
    /// <c>sync-token</c> or <c>sync-level</c> beside the <c>prop</c>. A null document, or a root
    /// with no child at all, is the empty body that means allprop and is valid.
    /// </summary>
    /// <param name="document">The parsed body, or null for an empty one.</param>
    /// <exception cref="DavBadRequestException">The body names no shape of the grammar.</exception>
    internal static void ValidatePropfindBody(XDocument? document)
    {
        if (document?.Root is not { } root) return;

        var shapes = 0;
        var includes = 0;
        foreach (var child in root.Elements())
        {
            if (child.Name == DavXml.Prop || child.Name == PropNameElement || child.Name == AllPropElement)
                shapes++;
            else if (child.Name == IncludeElement)
                includes++;
            else
                throw new DavBadRequestException(
                    $"A propfind body admits no {child.Name.LocalName} element.");
        }

        if (shapes > 1)
            throw new DavBadRequestException(
                "A propfind body names one of prop, propname or allprop, never several.");
        if (includes > 0 && root.Element(AllPropElement) is null)
            throw new DavBadRequestException("A propfind include is only valid beside an allprop.");
        if (includes > 1)
            throw new DavBadRequestException("A propfind allprop carries one include, never several.");
    }

    private static IReadOnlyList<XName> NamesIn(XElement container) =>
        [.. container.Elements().Select(e => e.Name).Distinct()];
}

/// <summary>The three shapes a PROPFIND body may take (RFC 4918 § 14.2, § 14.20, § 14.21).</summary>
internal enum DavPropertyMode
{
    Named,
    AllProp,
    PropName
}
