using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace weesky.Scotty.Microservice.Services.Bimi;

internal static partial class SvgGuard
{
    // SVG Tiny PS shapes and gradients, plus <use> (charged by SvgComplexity) and <style> (vetted below).
    // Paint servers that redraw content (pattern), filters, masks, clips, markers and text are refused:
    // each multiplies the render's cost beyond what SvgComplexity can bound, and no font ships with us.
    private static readonly HashSet<string> AllowedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "svg", "g", "defs", "title", "desc", "path", "rect", "circle", "ellipse", "line", "polyline", "polygon",
        "linearGradient", "radialGradient", "stop", "use", "style",
    };

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        MaxCharactersInDocument = 1_000_000,
    };

    public static bool IsSafe(ReadOnlySpan<byte> svg)
    {
        if (svg.IsEmpty) return false;

        try
        {
            using var reader = XmlReader.Create(new MemoryStream(svg.ToArray()), Settings);
            return Inspect(reader);
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool Inspect(XmlReader reader)
    {
        var sawRoot = false;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.ProcessingInstruction) return false;
            if (reader.NodeType != XmlNodeType.Element) continue;

            if (!sawRoot && reader.LocalName != "svg") return false;
            sawRoot = true;
            if (!AllowedElements.Contains(reader.LocalName) || !AttributesAreSafe(reader)) return false;
            if (reader.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase) && !StyleIsSafe(reader)) return false;
        }

        return sawRoot;
    }

    private static bool AttributesAreSafe(XmlReader reader)
    {
        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            var safe = !reader.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                && (reader.LocalName != "href" || reader.Value.StartsWith('#'))
                && !HasEscape(reader.Value)
                && !reader.Value.Contains("/*")
                && (Mentions(reader.LocalName) ? IsNoDash(reader.LocalName, reader.Value) : DashesAreInert(reader.Value))
                && !ExternalUrl().IsMatch(reader.Value);
            if (!safe) return false;
        }

        reader.MoveToElement();
        return true;
    }

    private static bool StyleIsSafe(XmlReader reader)
    {
        if (reader.IsEmptyElement) return true;

        var css = new StringBuilder();
        while (reader.Read() && reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType is not (XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)) return false;
            css.Append(reader.Value);
        }

        var text = css.ToString();
        return !HasEscape(text) && DashesAreInert(text) && !text.Contains('@') && !text.Contains("url(", StringComparison.OrdinalIgnoreCase)
            && FlatRules().IsMatch(Comment().Replace(text, ""));
    }

    private static bool HasEscape(string value) => value.Contains('\\');

    // Skia draws up to a million dashes per path whatever the element count; logos never dash, but
    // Inkscape writes "stroke-dasharray:none" everywhere. Every mention must be that one declaration.
    private static bool DashesAreInert(string css) =>
        css.Split([';', '{', '}']).All(declaration => !Mentions(declaration)
            || (declaration.Split(':', 2) is [var name, var value] && IsNoDash(name, value)));

    private static bool IsNoDash(string name, string value) =>
        name.Trim().Equals("stroke-dasharray", StringComparison.OrdinalIgnoreCase)
        && value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase);

    private static bool Mentions(string text) => text.Contains("dasharray", StringComparison.OrdinalIgnoreCase);

    // Rules over simple selectors only (type, .class, #id, *): no at-rule, combinator, attribute or
    // pseudo-class, since the renderer's selector matching backtracks through nested combinators.
    [GeneratedRegex(@"^(\s*[\w.#*-]+(\s*,\s*[\w.#*-]+)*\s*\{[^{}]*\})*\s*$", RegexOptions.NonBacktracking)]
    private static partial Regex FlatRules();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"url\(\s*['""]?\s*[^#'""\s]|javascript:", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalUrl();
}
