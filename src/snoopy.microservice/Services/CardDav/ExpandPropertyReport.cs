using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// The <c>DAV:expand-property</c> report (RFC 3253 § 3.8) — a double MUST here (RFC 6352 § 8.1,
/// RFC 3744 § 9.1) that iOS exercises at principal discovery. Each href-valued property the body
/// names has its hrefs substituted with nested <c>DAV:response</c> elements carrying the
/// properties the body's child <c>property</c> elements name, resolved on the resource each href
/// designates. The nesting depth is already bounded by <see cref="DavXmlReader.MaxDepth"/>.
/// </summary>
internal static class ExpandPropertyReport
{
    internal static async Task WriteAsync(HttpResponse response, XDocument body,
        DavResourceContext target, string targetHref,
        Func<DavResource, DavResourceContext?> nestedContext, CancellationToken cancellationToken)
    {
        List<XElement> found = [];
        List<XName> missing = [];
        ResolveAll(PropertiesOf(body.Root!), target, nestedContext, found, missing);

        await using var writer = await MultiStatusWriter.BeginAsync(response, cancellationToken);
        await writer.WriteResourceAsync(targetHref, found, missing, cancellationToken);
    }

    private static void ResolveAll(IReadOnlyList<Property> properties, DavResourceContext resource,
        Func<DavResource, DavResourceContext?> nestedContext, List<XElement> found, List<XName> missing)
    {
        foreach (var property in properties)
        {
            if (Resolve(property, resource, nestedContext) is { } element) found.Add(element);
            else missing.Add(property.Name);
        }
    }

    private static XElement? Resolve(Property property, DavResourceContext resource,
        Func<DavResource, DavResourceContext?> nestedContext)
    {
        var (found, _) = DavProperties.Resolve(
            new DavPropertyRequest(DavPropertyMode.Named, [property.Name]), resource);
        if (found.Count == 0) return null;

        var element = found[0];
        // A property with no children to expand, or no hrefs to expand into, is reported as a
        // PROPFIND would report it.
        if (property.Children.Count == 0 || !element.Elements(DavXml.Href).Any()) return element;

        var expanded = new XElement(element.Name);
        foreach (var href in element.Elements(DavXml.Href).Select(h => h.Value))
        {
            var context = DavPaths.Parse(href) is { } aimed ? nestedContext(aimed) : null;
            expanded.Add(context is null
                ? NotFound(href)
                : Nested(href, context, property.Children, nestedContext));
        }

        return expanded;
    }

    private static XElement Nested(string href, DavResourceContext resource,
        IReadOnlyList<Property> children, Func<DavResource, DavResourceContext?> nestedContext)
    {
        List<XElement> found = [];
        List<XName> missing = [];
        ResolveAll(children, resource, nestedContext, found, missing);

        var response = new XElement(DavXml.Response, new XElement(DavXml.Href, href));
        if (found.Count > 0) response.Add(PropStat(found, StatusCodes.Status200OK));
        if (missing.Count > 0)
            response.Add(PropStat(missing.Select(name => new XElement(name)), StatusCodes.Status404NotFound));
        return response;
    }

    private static XElement PropStat(IEnumerable<XElement> properties, int statusCode) =>
        new(DavXml.PropStat, new XElement(DavXml.Prop, properties),
            new XElement(DavXml.Status, MultiStatusWriter.StatusLine(statusCode)));

    private static XElement NotFound(string href) =>
        new(DavXml.Response, new XElement(DavXml.Href, href),
            new XElement(DavXml.Status, MultiStatusWriter.StatusLine(StatusCodes.Status404NotFound)));

    /// <summary>
    /// The body's <c>DAV:property</c> elements: a <c>name</c> attribute, an optional
    /// <c>namespace</c> one (<c>DAV:</c> by default, RFC 3253 § 3.8), children of the same shape.
    /// </summary>
    private static List<Property> PropertiesOf(XElement parent) =>
        [.. parent.Elements(DavXml.Dav + "property").Select(Read).OfType<Property>()];

    private static Property? Read(XElement element)
    {
        var name = element.Attribute("name")?.Value;
        if (string.IsNullOrEmpty(name)) return null;

        var ns = element.Attribute("namespace")?.Value ?? DavXml.Dav.NamespaceName;
        return new Property(XNamespace.Get(ns) + name, PropertiesOf(element));
    }

    private sealed record Property(XName Name, IReadOnlyList<Property> Children);
}
