using weesky.Snoopy.Microservice.Services.CardDav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CardDav;

public sealed class DavCollationTests
{
    [Fact]
    public void AsciiCasemap_FoldsAsciiOnly()
    {
        var comparer = DavCollation.Resolve(DavCollation.AsciiCasemap);

        Assert.Equal(0, comparer.Compare("ADA", "ada"));
        // "É" and "é" are DIFFERENT under i;ascii-casemap (RFC 4790 § 9.2.1). One
        // case-insensitive comparison for both collations would lie for this one on every accent.
        Assert.NotEqual(0, comparer.Compare("ÉLÉONORE", "éléonore"));
    }

    [Fact]
    public void UnicodeCasemap_FoldsEverything()
    {
        var comparer = DavCollation.Resolve(DavCollation.UnicodeCasemap);

        Assert.Equal(0, comparer.Compare("ÉLÉONORE", "éléonore"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("default")]
    public void AnAbsentAttributeOrTheLiteralDefault_MeanUnicodeCasemap(string? attribute)
    {
        // § 8.3 imposes it, and `default` falling into "unknown collation" would be a guaranteed
        // wrongful refusal on a conforming attribute.
        Assert.Equal(0, DavCollation.Resolve(attribute).Compare("É", "é"));
    }

    [Fact]
    public void AnUnknownCollation_IsRefusedWithItsOwnCondition()
    {
        var thrown = Assert.Throws<DavPreconditionException>(() => DavCollation.Resolve("i;octet"));

        // supported-collation and not supported-filter: the client must know whether its filter or
        // its collation is at fault. sabre answers a 400 with no condition and Radicale ignores the
        // attribute; the RFC's MUST says otherwise.
        Assert.Equal(DavXml.CardDav + "supported-collation", thrown.Condition);
    }

    [Theory]
    [InlineData("I;ASCII-CASEMAP")]
    [InlineData("i;Ascii-Casemap")]
    public void ACollationName_ComparesCaseInsensitively(string attribute)
    {
        // RFC 4790 § 3.1: collation names themselves compare case-insensitively. The É proves the
        // resolved collation really is the ASCII one, not the default the spelling fell back to.
        Assert.NotEqual(0, DavCollation.Resolve(attribute).Compare("É", "é"));
    }

    [Fact]
    public void UnicodeCasemap_EquatesComposedAndDecomposedSpellings()
    {
        // U+00E9 and U+0065 U+0301 are the same letter to RFC 5051, and iOS emits the decomposed
        // form; without normalization the fold would compare the spellings, not the letter.
        Assert.Equal(0, DavCollation.Resolve(null).Compare("é", "e\u0301"));
        Assert.NotEqual(0, DavCollation.Resolve(DavCollation.AsciiCasemap).Compare("é", "e\u0301"));
    }
}
