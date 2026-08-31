using System.Text;
using System.Xml.Linq;
using weesky.Snoopy.Microservice.Services.CardDav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CardDav;

public sealed class AddressDataFilterTests
{
    [Fact]
    public void RestrictingToOneProperty_KeepsTheCardAValidCard()
    {
        const string card =
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\nEMAIL:a@b.c\r\nTEL:+32\r\nEND:VCARD\r\n";

        var restricted = AddressDataFilter.Restrict(card, ["EMAIL"]);

        // Without BEGIN, END, VERSION and UID what comes out is not a card at all.
        Assert.Contains("BEGIN:VCARD", restricted);
        Assert.Contains("END:VCARD", restricted);
        Assert.Contains("VERSION:3.0", restricted);
        Assert.Contains("UID:u1", restricted);
        Assert.Contains("EMAIL:a@b.c", restricted);
        Assert.DoesNotContain("TEL:", restricted);
        Assert.DoesNotContain("FN:", restricted);
    }

    [Fact]
    public void RestrictingToNothing_ReturnsTheWholeCard()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\nEND:VCARD\r\n";

        // An address-data with no prop children means "the whole card", not "nothing".
        Assert.Equal(card, AddressDataFilter.Restrict(card, []));
    }

    [Fact]
    public void RestrictingKeepsAGroupedProperty_WhenItsNameMatches()
    {
        const string card =
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nitem1.EMAIL:a@b.c\r\nitem1.X-ABLabel:Work\r\nEND:VCARD\r\n";

        var restricted = AddressDataFilter.Restrict(card, ["EMAIL"]);

        // The group prefix is not the property name. Comparing the whole "item1.EMAIL" would drop a
        // property the client did ask for.
        Assert.Contains("item1.EMAIL:a@b.c", restricted);
        Assert.DoesNotContain("X-ABLabel", restricted);
    }

    [Fact]
    public void RestrictingKeepsAFoldedLineWhole()
    {
        var card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nNOTE:" + new string('a', 100) +
                   "\r\nEND:VCARD\r\n";

        var restricted = AddressDataFilter.Restrict(FoldedForm(card), ["NOTE"]);

        // A continuation line begins with a space and carries no name of its own: dropping it would
        // truncate the value it continues.
        Assert.Contains(new string('a', 100), Unfold(restricted));
    }

    [Fact]
    public void RestrictingIsCaseInsensitiveOnTheRequestedName()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nEmail:a@b.c\r\nTEL:+32\r\nEND:VCARD\r\n";

        var restricted = AddressDataFilter.Restrict(card, ["email"]);

        // vCard property names are case-insensitive: a card spelling one in mixed case is not a
        // card that omits it.
        Assert.Contains("Email:a@b.c", restricted);
        Assert.DoesNotContain("TEL:", restricted);
    }

    [Theory]
    [InlineData("2.1")]
    [InlineData("5.0")]
    [InlineData("")]
    public void AVersionOutsideWhatWeAnnounce_IsRefused(string version)
    {
        var element = AddressDataElement(version: version);

        var thrown = Assert.Throws<DavPreconditionException>(() => AddressDataFilter.Parse(element));
        Assert.Equal(DavXml.CardDav + "supported-address-data", thrown.Condition);
    }

    [Fact]
    public void AContentTypeThatIsNotVcard_IsRefused()
    {
        var element = AddressDataElement(contentType: "text/plain");

        var thrown = Assert.Throws<DavPreconditionException>(() => AddressDataFilter.Parse(element));
        Assert.Equal(DavXml.CardDav + "supported-address-data", thrown.Condition);
    }

    [Fact]
    public void NoVersionAttribute_MeansAsStored()
    {
        Assert.Null(AddressDataFilter.Parse(AddressDataElement()).Version);
    }

    [Theory]
    [InlineData("3.0")]
    [InlineData("4.0")]
    public void TheTwoVersionsWeAnnounce_AreAccepted(string version)
    {
        Assert.Equal(version, AddressDataFilter.Parse(AddressDataElement(version: version)).Version);
    }

    [Theory]
    [InlineData("text/vcard")]
    [InlineData("TEXT/VCARD")]
    [InlineData("text/vcard; charset=utf-8")]
    public void TheContentTypeWeServe_IsAccepted(string contentType)
    {
        // A media type is case-insensitive and may carry parameters; refusing either spelling would
        // refuse a client that asked for exactly what we serve.
        var request = AddressDataFilter.Parse(AddressDataElement(contentType: contentType));

        Assert.Null(request.Version);
        Assert.Empty(request.PropertyNames);
    }

    [Fact]
    public void TheRequestedPropertyNames_AreReadInOrder()
    {
        var element = AddressDataElement(props: ["EMAIL", "TEL"]);

        Assert.Equal(["EMAIL", "TEL"], AddressDataFilter.Parse(element).PropertyNames);
    }

    [Fact]
    public void APropOfAnotherNamespace_IsNotAnAddressDataProp()
    {
        // DAV: has a prop element of its own; an element is its namespace and local name, never its
        // prefix, and reading the wrong one would restrict the card to properties nobody asked for.
        var element = new XElement(DavXml.CardDav + "address-data",
            new XElement(DavXml.Dav + "prop", new XAttribute("name", "EMAIL")));

        Assert.Empty(AddressDataFilter.Parse(element).PropertyNames);
    }

    private static XElement AddressDataElement(
        string? version = null, string? contentType = null, params string[] props)
    {
        var element = new XElement(DavXml.CardDav + "address-data");
        if (version is not null) element.Add(new XAttribute("version", version));
        if (contentType is not null) element.Add(new XAttribute("content-type", contentType));
        foreach (var name in props)
            element.Add(new XElement(DavXml.CardDav + "prop", new XAttribute("name", name)));
        return element;
    }

    // Folded and unfolded here rather than through the production primitives: a fixture built by
    // the code under test proves only that it agrees with itself.
    private static string FoldedForm(string card) => string.Join("\r\n",
        card.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Select(Wrap)) + "\r\n";

    private static string Wrap(string line)
    {
        if (line.Length <= 75) return line;
        var builder = new StringBuilder(line[..75]);
        for (var at = 75; at < line.Length; at += 74)
            builder.Append("\r\n ").Append(line, at, Math.Min(74, line.Length - at));
        return builder.ToString();
    }

    private static string Unfold(string text) => text.Replace("\r\n ", string.Empty);
}
