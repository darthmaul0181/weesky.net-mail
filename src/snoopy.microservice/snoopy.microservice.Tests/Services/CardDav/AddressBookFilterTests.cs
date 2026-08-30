using System.Xml.Linq;
using weesky.Snoopy.Microservice.Services.CardDav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CardDav;

public sealed class AddressBookFilterTests
{
    private const string Card =
        "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada Lovelace\r\nTITLE:Analyst\r\n" +
        "item1.TEL;TYPE=CELL:+3210\r\nEMAIL;TYPE=WORK:ada@weesky.be\r\nEND:VCARD\r\n";

    [Fact]
    public void AFilterWithNoChildren_MatchesTheWholeBook()
    {
        // Tricky because the general rule gives the WRONG answer here: anyof over zero tests is
        // false, so an empty <filter/> would keep nothing and the client would get an empty book
        // where it asked for everything. It is the shape several clients send for "give me what you
        // have", and sabre treats it so on its evaluator's first line.
        var spec = AddressBookFilter.Parse(FilterElement());

        Assert.True(AddressBookFilter.Matches(Card, spec));
    }

    [Fact]
    public void APropFilterOnAProjectedColumn_Matches() =>
        Assert.True(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("FN", TextMatch("lovelace")))));

    [Fact]
    public void APropFilterOnAPropertyWeDoNotProject_MatchesToo()
    {
        // Restricting evaluation to projected columns would answer 403 supported-filter to perfectly
        // ordinary filters. The book holds vcard_raw and 4a supplies the parser: this is what
        // separates a usable server from one that refuses half the requests it is sent.
        Assert.True(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("TITLE", TextMatch("analyst")))));
    }

    [Fact]
    public void APropFilterMatchesAGroupedProperty()
    {
        // A MUST of § 10.5.1 that iOS cards exercise everywhere: TEL matches item1.TEL.
        Assert.True(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("TEL", TextMatch("3210")))));
    }

    [Fact]
    public void APropertyNameIsCaseInsensitive() =>
        Assert.True(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("fn", TextMatch("Ada")))));

    [Fact]
    public void APropertyAbsentFromTheCard_FailsWithoutAnError()
    {
        // A filter that keeps nothing, not a filter we do not understand. The distinction is what
        // keeps 403 supported-filter a SIGNAL rather than the report's ordinary return code.
        Assert.False(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("NICKNAME", TextMatch("x")))));
    }

    [Fact]
    public void IsNotDefined_IsEvaluated()
    {
        Assert.True(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("NICKNAME", IsNotDefined()))));
        Assert.False(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("FN", IsNotDefined()))));
    }

    [Fact]
    public void AParamFilter_IsEvaluatedOnTheRetainedProperty()
    {
        Assert.True(AddressBookFilter.Matches(Card,
            ParseFilter(PropFilter("TEL", ParamFilter("TYPE", TextMatch("CELL"))))));
        Assert.False(AddressBookFilter.Matches(Card,
            ParseFilter(PropFilter("TEL", ParamFilter("TYPE", TextMatch("FAX"))))));
    }

    [Theory]
    [InlineData("contains", "ovela", true)]
    [InlineData("equals", "Ada Lovelace", true)]
    [InlineData("equals", "Ada", false)]
    [InlineData("starts-with", "Ada", true)]
    [InlineData("starts-with", "Lovelace", false)]
    [InlineData("ends-with", "Lovelace", true)]
    public void TheFourMatchTypes_AreEvaluated(string matchType, string value, bool expected) =>
        Assert.Equal(expected,
            AddressBookFilter.Matches(Card, ParseFilter(PropFilter("FN", TextMatch(value, matchType)))));

    [Fact]
    public void AnAbsentMatchType_MeansContains() =>
        Assert.True(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("FN", TextMatch("ovela")))));

    [Fact]
    public void NegateCondition_Inverts()
    {
        Assert.False(AddressBookFilter.Matches(Card,
            ParseFilter(PropFilter("FN", TextMatch("Ada", negate: true)))));
        Assert.True(AddressBookFilter.Matches(Card,
            ParseFilter(PropFilter("FN", TextMatch("Grace", negate: true)))));
    }

    [Theory]
    [InlineData("anyof", true)]
    [InlineData("allof", false)]
    public void TheFilterTest_CombinesItsPropFilters(string test, bool expected)
    {
        var spec = ParseFilter(
            PropFilter("FN", TextMatch("Ada")),
            PropFilter("NICKNAME", TextMatch("nope")),
            test: test);

        Assert.Equal(expected, AddressBookFilter.Matches(Card, spec));
    }

    [Fact]
    public void AnAbsentTest_MeansAnyof() =>
        Assert.True(AddressBookFilter.Matches(Card,
            ParseFilter(PropFilter("FN", TextMatch("Ada")), PropFilter("NICKNAME", TextMatch("nope")))));

    [Theory]
    [InlineData("anyof", true)]
    [InlineData("allof", false)]
    public void ThePropFilterTest_CombinesItsOwnChildren(string test, bool expected)
    {
        var spec = ParseFilter(PropFilter("FN",
            test: test, TextMatch("Ada"), TextMatch("Grace")));

        Assert.Equal(expected, AddressBookFilter.Matches(Card, spec));
    }

    [Theory]
    [InlineData("comp-filter")]
    [InlineData("time-range")]
    [InlineData("some-vendor-extension")]
    public void AnythingTheTableDoesNotName_IsRefused(string localName)
    {
        var thrown = Assert.Throws<DavPreconditionException>(() =>
            AddressBookFilter.Parse(FilterElement(new XElement(DavXml.CardDav + localName))));

        // Answering "the whole book" to a filter we do not understand looks like success and hands
        // the client a FALSE result set, which it writes into its cache.
        Assert.Equal(DavXml.CardDav + "supported-filter", thrown.Condition);
    }

    [Fact]
    public void AnUnknownCollationInATextMatch_IsRefusedWithTheCollationCondition()
    {
        var thrown = Assert.Throws<DavPreconditionException>(() =>
            AddressBookFilter.Parse(FilterElement(PropFilter("FN", TextMatch("x", collation: "i;octet")))));

        Assert.Equal(DavXml.CardDav + "supported-collation", thrown.Condition);
    }

    // ---- beyond the brief: the collation plumbed into a match, and the strictness table ----

    [Fact]
    public void TheCollationInsideATextMatch_IsTheOneEvaluated()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u2\r\nFN:Éléonore\r\nEND:VCARD\r\n";

        // The same card, the same value, two answers: only a character outside ASCII can tell the
        // two collations apart, so only one can prove the attribute reaches the comparison.
        Assert.True(AddressBookFilter.Matches(card,
            ParseFilter(PropFilter("FN", TextMatch("éléonore", "equals")))));
        Assert.False(AddressBookFilter.Matches(card, ParseFilter(PropFilter("FN",
            TextMatch("éléonore", "equals", collation: DavCollation.AsciiCasemap)))));
    }

    [Fact]
    public void APropFilterOfAnotherNamespace_IsNotAPropFilter()
    {
        // An element is its namespace and local name, never its prefix: DAV:prop-filter is not
        // CARDDAV:prop-filter however it is spelled.
        var thrown = Assert.Throws<DavPreconditionException>(() => AddressBookFilter.Parse(
            FilterElement(new XElement(DavXml.Dav + "prop-filter", new XAttribute("name", "FN")))));

        Assert.Equal(DavXml.CardDav + "supported-filter", thrown.Condition);
    }

    [Fact]
    public void AnUnknownMatchType_IsRefusedAsAFilter()
    {
        var thrown = Assert.Throws<DavPreconditionException>(() =>
            AddressBookFilter.Parse(FilterElement(PropFilter("FN", TextMatch("x", "fuzzy")))));

        Assert.Equal(DavXml.CardDav + "supported-filter", thrown.Condition);
    }

    [Fact]
    public void APropFilterWithNoChildren_MatchesWhenThePropertyExists()
    {
        Assert.True(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("FN"))));
        Assert.False(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("NICKNAME"))));
    }

    [Fact]
    public void IsNotDefinedInsideAParamFilter_IsEvaluated()
    {
        // "TEL without TYPE" is a real query; inverted, the client would receive exactly the
        // opposite set with nothing looking wrong.
        Assert.False(AddressBookFilter.Matches(Card,
            ParseFilter(PropFilter("TEL", ParamFilter("TYPE", IsNotDefined())))));
        Assert.True(AddressBookFilter.Matches(Card,
            ParseFilter(PropFilter("TEL", ParamFilter("X-NOPE", IsNotDefined())))));
    }

    [Fact]
    public void AnEscapedValue_IsMatchedOnItsTextNotItsWireForm()
    {
        // FN:Ada\, Jr is the wire spelling of "Ada, Jr": the client searches the text it typed.
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u4\r\nFN:Ada\\, Jr\r\nEND:VCARD\r\n";

        Assert.True(AddressBookFilter.Matches(card,
            ParseFilter(PropFilter("FN", TextMatch("Ada, Jr", "equals")))));
        Assert.False(AddressBookFilter.Matches(card,
            ParseFilter(PropFilter("FN", TextMatch("Ada\\, Jr", "equals")))));
    }

    [Fact]
    public void MalformedShapes_AreRefusedAsFilters()
    {
        var shapes = new[]
        {
            WithAttribute(FilterElement(PropFilter("FN")), "test", "noneof"),
            FilterElement(WithAttribute(PropFilter("FN"), "test", "noneof")),
            FilterElement(PropFilter("FN", WithAttribute(TextMatch("x"), "negate-condition", "maybe"))),
            // § 10.5.1 makes is-not-defined exclusive of its siblings.
            FilterElement(PropFilter("FN", IsNotDefined(), TextMatch("x"))),
            // (is-not-defined | text-match?): a second child of param-filter has no defined meaning.
            FilterElement(PropFilter("TEL", ParamFilter("TYPE", TextMatch("a"), TextMatch("b")))),
            FilterElement(new XElement(DavXml.CardDav + "prop-filter")),
        };

        foreach (var shape in shapes)
        {
            var thrown = Assert.Throws<DavPreconditionException>(() => AddressBookFilter.Parse(shape));
            Assert.Equal(DavXml.CardDav + "supported-filter", thrown.Condition);
        }
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static XElement WithAttribute(XElement element, string name, string value)
    {
        element.SetAttributeValue(name, value);
        return element;
    }

    private static XElement FilterElement(params XElement[] children) =>
        new(DavXml.CardDav + "filter", children);

    private static AddressBookFilterSpec ParseFilter(params XElement[] propFilters) =>
        AddressBookFilter.Parse(FilterElement(propFilters));

    private static AddressBookFilterSpec ParseFilter(XElement first, XElement second, string test)
    {
        var element = FilterElement(first, second);
        element.Add(new XAttribute("test", test));
        return AddressBookFilter.Parse(element);
    }

    private static XElement PropFilter(string name, params XElement[] children) =>
        PropFilter(name, null, children);

    private static XElement PropFilter(string name, string? test, params XElement[] children)
    {
        var element = new XElement(DavXml.CardDav + "prop-filter", new XAttribute("name", name));
        if (test is not null) element.Add(new XAttribute("test", test));
        element.Add(children.Cast<object>().ToArray());
        return element;
    }

    private static XElement TextMatch(
        string value, string? matchType = null, bool negate = false, string? collation = null)
    {
        var element = new XElement(DavXml.CardDav + "text-match", value);
        if (matchType is not null) element.Add(new XAttribute("match-type", matchType));
        if (negate) element.Add(new XAttribute("negate-condition", "yes"));
        if (collation is not null) element.Add(new XAttribute("collation", collation));
        return element;
    }

    private static XElement IsNotDefined() => new(DavXml.CardDav + "is-not-defined");

    private static XElement ParamFilter(string name, params XElement[] children)
    {
        var element = new XElement(DavXml.CardDav + "param-filter", new XAttribute("name", name));
        element.Add(children.Cast<object>().ToArray());
        return element;
    }
}
