using System.Text;
using System.Xml.Linq;
using weesky.Snoopy.Microservice.Services.Contacts;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// The addressbook-query filter, parsed strictly and evaluated on the card itself — never on the
/// projected columns, which would answer 403 to a perfectly ordinary prop-filter on TITLE or
/// NICKNAME. Anything the grammar does not name is refused with <c>supported-filter</c>: answering
/// « the whole book » to a filter we do not understand looks like success and hands the client a
/// false result set, which it writes into its cache. A property absent from the card, by contrast,
/// fails its prop-filter without any error — a filter that keeps nothing, not one we do not
/// understand — which is what keeps the 403 a signal rather than the report's ordinary answer.
/// </summary>
internal static class AddressBookFilter
{
    private static readonly XName PropFilterName = DavXml.CardDav + "prop-filter";
    private static readonly XName TextMatchName = DavXml.CardDav + "text-match";
    private static readonly XName IsNotDefinedName = DavXml.CardDav + "is-not-defined";
    private static readonly XName ParamFilterName = DavXml.CardDav + "param-filter";

    /// <summary>
    /// Parses the filter, or throws <see cref="DavPreconditionException"/>
    /// (<c>supported-filter</c>) on anything RFC 6352 § 10.5 does not name — an unknown collation
    /// alone answers <c>supported-collation</c> instead. A filter with no children parses to the
    /// whole book.
    /// </summary>
    internal static AddressBookFilterSpec Parse(XElement filter)
    {
        var propFilters = new List<PropFilterSpec>();
        foreach (var child in filter.Elements())
        {
            if (child.Name != PropFilterName) throw Refused();
            propFilters.Add(ParsePropFilter(child));
        }

        return new AddressBookFilterSpec(AllOf(filter), propFilters);
    }

    /// <summary>Whether one card satisfies the filter.</summary>
    internal static bool Matches(string vCardRaw, AddressBookFilterSpec spec)
    {
        // The special case first, before any test logic: several clients spell « give me what you
        // have » as an empty filter, and anyof over zero tests would keep nothing at all.
        if (spec.PropFilters.Count == 0) return true;
        var properties = Properties(vCardRaw);
        return Combined(spec.AllOf, spec.PropFilters.Select(f => MatchesPropFilter(properties, f)));
    }

    // ---- evaluation, on the card's logical lines ------------------------------------------------

    private static bool MatchesPropFilter(List<CardProperty> properties, PropFilterSpec filter)
    {
        // NameOf already strips the group prefix: TEL matches item1.TEL, a MUST of § 10.5.1 that
        // iOS cards exercise everywhere.
        var instances = properties
            .Where(p => p.Name.Equals(filter.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (filter.IsNotDefined) return instances.Count == 0;
        if (instances.Count == 0) return false;
        if (filter.TextMatches.Count == 0 && filter.ParamFilters.Count == 0) return true;
        return Combined(filter.AllOf, filter.TextMatches
            .Select(t => MatchesValue(instances, t))
            .Concat(filter.ParamFilters.Select(p => MatchesParam(instances, p))));
    }

    private static bool MatchesValue(List<CardProperty> instances, TextMatchSpec match)
    {
        var satisfied = instances.Any(i => match.Satisfies(i.Value));
        return match.Negate ? !satisfied : satisfied;
    }

    private static bool MatchesParam(List<CardProperty> instances, ParamFilterSpec filter)
    {
        if (filter.IsNotDefined) return !instances.Any(i => HasParameter(i, filter.Name));
        if (filter.TextMatch is not { } match)
            return instances.Any(i => HasParameter(i, filter.Name));
        var satisfied = instances.Any(i => ParameterValues(i, filter.Name).Any(match.Satisfies));
        return match.Negate ? !satisfied : satisfied;
    }

    private static bool Combined(bool allOf, IEnumerable<bool> results) =>
        allOf ? results.All(r => r) : results.Any(r => r);

    private static List<CardProperty> Properties(string vCardRaw)
    {
        var properties = new List<CardProperty>();
        foreach (var line in VCardComposer.LogicalLines(VCardComposer.CanonicalLineBreaks(vCardRaw)))
        {
            var unfolded = VCardComposer.Unfold(line);
            var name = VCardComposer.NameOf(unfolded);
            if (name.Equals("END", StringComparison.OrdinalIgnoreCase)) break;
            var colon = VCardComposer.IndexOutsideQuotes(unfolded, ':');
            if (name.Length == 0 || colon < 0) continue;
            var head = unfolded[..colon];
            var semi = VCardComposer.IndexOutsideQuotes(head, ';');
            var parameters = semi < 0 ? [] : VCardComposer.SplitOutsideQuotes(head[(semi + 1)..]);
            properties.Add(new CardProperty(name, parameters, Unescaped(unfolded[(colon + 1)..])));
        }

        return properties;
    }

    private static bool HasParameter(CardProperty property, string name) =>
        property.Parameters.Any(p =>
            VCardComposer.KeyOf(p).Equals(name, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> ParameterValues(CardProperty property, string name)
    {
        foreach (var parameter in property.Parameters)
        {
            if (!VCardComposer.KeyOf(parameter).Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            var eq = parameter.IndexOf('=');
            if (eq < 0) continue;
            foreach (var value in SplitValues(parameter[(eq + 1)..])) yield return value;
        }
    }

    // TYPE=CELL,VOICE names two values; a quoted value keeps its commas and loses only its quotes.
    private static IEnumerable<string> SplitValues(string values)
    {
        var start = 0;
        var inQuotes = false;
        for (var i = 0; i <= values.Length; i++)
        {
            if (i < values.Length && values[i] == '"') { inQuotes = !inQuotes; continue; }
            if (i < values.Length && (values[i] != ',' || inQuotes)) continue;
            yield return values[start..i].Trim().Trim('"');
            start = i + 1;
        }
    }

    // The wire escapes spelled plainly again: the client matches on the text, not on its encoding.
    private static string Unescaped(string value)
    {
        if (!value.Contains('\\')) return value;
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 == value.Length)
            {
                builder.Append(value[i]);
                continue;
            }

            var next = value[++i];
            builder.Append(next is 'n' or 'N' ? '\n' : next);
        }

        return builder.ToString();
    }

    private readonly record struct CardProperty(string Name, List<string> Parameters, string Value);

    // ---- parsing --------------------------------------------------------------------------------

    private static PropFilterSpec ParsePropFilter(XElement element)
    {
        var name = DavXml.Attribute(element, "name");
        if (string.IsNullOrEmpty(name)) throw Refused();
        var isNotDefined = false;
        var textMatches = new List<TextMatchSpec>();
        var paramFilters = new List<ParamFilterSpec>();
        foreach (var child in element.Elements())
        {
            if (child.Name == IsNotDefinedName) isNotDefined = true;
            else if (child.Name == TextMatchName) textMatches.Add(ParseTextMatch(child));
            else if (child.Name == ParamFilterName) paramFilters.Add(ParseParamFilter(child));
            else throw Refused();
        }

        // The grammar's is-not-defined excludes the other children (§ 10.5.1).
        if (isNotDefined && textMatches.Count + paramFilters.Count > 0) throw Refused();
        return new PropFilterSpec(name, AllOf(element), isNotDefined, textMatches, paramFilters);
    }

    private static ParamFilterSpec ParseParamFilter(XElement element)
    {
        var name = DavXml.Attribute(element, "name");
        if (string.IsNullOrEmpty(name)) throw Refused();
        var children = element.Elements().ToList();
        // (is-not-defined | text-match?): a second child has no meaning the RFC defines.
        if (children.Count > 1) throw Refused();
        var child = children.FirstOrDefault();
        if (child is null) return new ParamFilterSpec(name, false, null);
        if (child.Name == IsNotDefinedName) return new ParamFilterSpec(name, true, null);
        if (child.Name == TextMatchName) return new ParamFilterSpec(name, false, ParseTextMatch(child));
        throw Refused();
    }

    private static TextMatchSpec ParseTextMatch(XElement element)
    {
        var comparer = DavCollation.Resolve(DavXml.Attribute(element, "collation"));
        var matchType = DavXml.Attribute(element, "match-type") switch
        {
            null or "contains" => TextMatchKind.Contains,
            "equals" => TextMatchKind.Equals,
            "starts-with" => TextMatchKind.StartsWith,
            "ends-with" => TextMatchKind.EndsWith,
            _ => throw Refused(),
        };
        var negate = DavXml.Attribute(element, "negate-condition") switch
        {
            null or "no" => false,
            "yes" => true,
            _ => throw Refused(),
        };
        return new TextMatchSpec(element.Value, matchType, negate, comparer);
    }

    private static bool AllOf(XElement element) => DavXml.Attribute(element, "test") switch
    {
        null or "anyof" => false,
        "allof" => true,
        _ => throw Refused(),
    };

    private static DavPreconditionException Refused() =>
        new(DavXml.CardDav + "supported-filter");
}
