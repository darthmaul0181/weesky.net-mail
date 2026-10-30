using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace weesky.Scotty.Microservice.Services.Bimi;

/// <summary>
/// Bounds the work a drawing can ask of the renderer before it sees it: every element counts, a shape
/// one more per point of its geometry (stroking and filling are linear in it), every
/// reference (<c>href="#x"</c>, <c>url(#x)</c>) counts the whole of its target again, and a chain of
/// nesting and references longer than <see cref="MaxPath"/> is refused, since the renderer recurses
/// through both and overflowing its stack would kill the process rather than throw.
/// </summary>
internal static partial class SvgComplexity
{
    public const int MaxCost = 10_000;
    public const int MaxPath = 48;
    private const int Over = MaxCost + 1;

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        MaxCharactersInDocument = 1_000_000,
    };

    public static bool IsBounded(ReadOnlySpan<byte> svg)
    {
        XElement root;
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(svg.ToArray()), Settings);
            root = XDocument.Load(reader).Root!;
        }
        catch (XmlException)
        {
            return false;
        }

        var drawing = new Drawing(root);
        return drawing.Cost(root) + drawing.StyleCost() <= MaxCost;
    }

    [GeneratedRegex(@"url\(\s*['""]?\s*#([^'""\s)]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LocalUrl();

    [GeneratedRegex(@"[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?")]
    private static partial Regex Number();

    private sealed class Drawing(XElement root)
    {
        private readonly Dictionary<string, XElement> ids = root.DescendantsAndSelf()
            .Select(e => (Id: (string?)e.Attribute("id"), Element: e))
            .Where(p => p.Id is not null)
            .DistinctBy(p => p.Id)
            .ToDictionary(p => p.Id!, p => p.Element);

        private readonly Dictionary<XElement, int> costs = [];
        private readonly HashSet<XElement> path = [];

        public int Cost(XElement element)
        {
            if (costs.TryGetValue(element, out var known)) return known;
            if (path.Count >= MaxPath || !path.Add(element)) return Over;

            var total = Math.Min(Weight(element), Over);
            foreach (var part in element.Elements().Concat(References(element)))
                total = Math.Min(total + Cost(part), Over);

            path.Remove(element);
            return costs[element] = (int)total;
        }

        public long StyleCost()
        {
            var elements = root.DescendantsAndSelf().Count();
            return root.Descendants().Where(e => e.Name.LocalName == "style")
                .SelectMany(style => LocalUrl().Matches(style.Value))
                .Select(match => ids.GetValueOrDefault(match.Groups[1].Value))
                .OfType<XElement>()
                .Aggregate(0L, (sum, target) => Math.Min(sum + (long)elements * Cost(target), Over));
        }

        private static long Weight(XElement element) =>
            1 + element.Attributes().Where(a => a.Name.LocalName is "d" or "points").Sum(a => (long)Number().Count(a.Value)) / 2;

        private IEnumerable<XElement> References(XElement element) =>
            element.Attributes()
                .SelectMany(a => a.Name.LocalName == "href" && a.Value.StartsWith('#')
                    ? [a.Value[1..]]
                    : LocalUrl().Matches(a.Value).Select(m => m.Groups[1].Value))
                .Select(id => ids.GetValueOrDefault(id))
                .OfType<XElement>();
    }
}
