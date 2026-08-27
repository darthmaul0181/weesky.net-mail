using weesky.Snoopy.Microservice.Services.CardDav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CardDav;

public sealed class DavPathsTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void ACollectionHref_AlwaysCarriesItsTrailingSlash()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // A client compares hrefs literally: the collection always has its slash, a card never has
        // one. Getting this wrong makes a client treat two spellings of one resource as two.
        Assert.EndsWith("/", DavPaths.Collection(userId));
        Assert.EndsWith("/", DavPaths.Home(userId));
        Assert.EndsWith("/", DavPaths.Principal(userId));
        Assert.DoesNotContain("//dav", DavPaths.Collection(userId));
    }

    [Fact]
    public void ACardHref_CarriesNoTrailingSlashAndIsEscapedSegmentWise()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var href = DavPaths.Card(userId, "un nom#?.vcf");

        Assert.DoesNotContain(' ', href);
        Assert.DoesNotContain('#', href);
        Assert.DoesNotContain('?', href);
        // Without the escape, a name carrying a space, a '#' or a '?' — which a client may choose —
        // produces an href that same client cannot read back.
        Assert.Equal($"/dav/addressbooks/{userId}/default/{Uri.EscapeDataString("un nom#?.vcf")}", href);
    }

    [Fact]
    public void AnHref_IsNeverAFullUrl()
    {
        // The service is behind a reverse proxy: an absolute URL rebuilt from the host Kestrel sees
        // is not the one the client asked for.
        Assert.StartsWith("/", DavPaths.Collection(Guid.NewGuid()));
        Assert.DoesNotContain("://", DavPaths.Collection(Guid.NewGuid()));
    }

    [Fact]
    public void EachShapeOfPath_ResolvesToItsKind()
    {
        // A [Theory] carrying the kind would need a public parameter of an internal enum; the five
        // cases are asserted here rather than widening DavResourceKind past the assembly.
        Assert.Equal(DavResourceKind.ServiceRoot, DavPaths.Parse("/dav/")!.Kind);
        Assert.Equal(DavResourceKind.Principal,
            DavPaths.Parse("/dav/principals/11111111-1111-1111-1111-111111111111/")!.Kind);
        Assert.Equal(DavResourceKind.Home,
            DavPaths.Parse("/dav/addressbooks/11111111-1111-1111-1111-111111111111/")!.Kind);
        Assert.Equal(DavResourceKind.Collection,
            DavPaths.Parse("/dav/addressbooks/11111111-1111-1111-1111-111111111111/default/")!.Kind);
        Assert.Equal(DavResourceKind.Card,
            DavPaths.Parse("/dav/addressbooks/11111111-1111-1111-1111-111111111111/default/a.vcf")!.Kind);
    }

    [Fact]
    public void AnEncodedSegment_IsDecodedExactlyOnce()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var resource = DavPaths.Parse($"/dav/addressbooks/{userId}/default/un%20nom.vcf");

        Assert.Equal("un nom.vcf", resource!.DavName);
    }

    [Fact]
    public void ADoubleEncodedSlash_DoesNotComeBackAsATraversal()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // %252F decoded twice is '/'. Decoding once gives "%2F", which IsValid then accepts as a
        // literal name — no traversal. This is the assertion that says "once" is enforced.
        var resource = DavPaths.Parse($"/dav/addressbooks/{userId}/default/a%252Fb.vcf");

        Assert.Equal("a%2Fb.vcf", resource!.DavName);
    }

    [Fact]
    public void AnEncodedSlash_DecodesToAnInvalidNameRatherThanTraversing()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // %2F decodes to '/', which DavName refuses. Validating BEFORE the decode would let this
        // through to a store that refuses it later — or does not.
        var resource = DavPaths.Parse($"/dav/addressbooks/{userId}/default/a%2Fb.vcf");

        Assert.False(DavName.IsValid(resource!.DavName));
    }

    [Theory]
    [InlineData("/dav/addressbooks/not-a-guid/default/")]
    [InlineData("/dav/addressbooks/11111111-1111-1111-1111-111111111111/other/")]
    [InlineData("/api/contacts")]
    [InlineData("/dav/addressbooks/11111111-1111-1111-1111-111111111111/default/a/b")]
    public void APathThatIsNotOurs_ResolvesToNothing(string path) =>
        Assert.Null(DavPaths.Parse(path));

    [Fact]
    public void BuildingThenParsing_RoundTripsAnAwkwardName()
    {
        var userId = Guid.NewGuid();
        const string name = "Ada & Grace #1 ?.vcf";

        var parsed = DavPaths.Parse(DavPaths.Card(userId, name));

        Assert.Equal(name, parsed!.DavName);
        Assert.Equal(userId, parsed.UserId);
    }

    [Fact]
    public void EveryKindButACard_CarriesNoName()
    {
        Assert.Null(DavPaths.Parse("/dav/")!.DavName);
        Assert.Null(DavPaths.Parse(DavPaths.Principal(User))!.DavName);
        Assert.Null(DavPaths.Parse(DavPaths.Home(User))!.DavName);
        Assert.Null(DavPaths.Parse(DavPaths.Collection(User))!.DavName);
    }

    [Fact]
    public void TheServiceRoot_CarriesTheEmptyUser()
    {
        // Nothing under /dav/ names a user, and a caller reading UserId there would read a value
        // that means nothing rather than one that is absent.
        Assert.Equal(Guid.Empty, DavPaths.Parse("/dav/")!.UserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/dav")]                                     // a collection is spelled with its slash
    [InlineData("//dav/")]                                   // scheme-relative: the authority is not ours
    [InlineData("//example.test/dav/addressbooks/11111111-1111-1111-1111-111111111111/default/")]
    [InlineData("https://example.test/dav/addressbooks/11111111-1111-1111-1111-111111111111/default/")]
    [InlineData("dav/addressbooks/11111111-1111-1111-1111-111111111111/default/")]
    [InlineData("/DAV/addressbooks/11111111-1111-1111-1111-111111111111/default/")]
    [InlineData("/dav/addressbooks/")]
    [InlineData("/dav/addressbooks/11111111-1111-1111-1111-111111111111/default")] // collection, no slash
    [InlineData("/dav/addressbooks/11111111-1111-1111-1111-111111111111/default//")]
    [InlineData("/dav/addressbooks/{11111111-1111-1111-1111-111111111111}/default/")]
    [InlineData("/dav/addressbooks/11111111111111111111111111111111/default/")]
    [InlineData("/dav/principals/11111111-1111-1111-1111-111111111111")]           // principal, no slash
    [InlineData("/dav/principals/11111111-1111-1111-1111-111111111111/x/")]
    [InlineData("/dav/../dav/addressbooks/11111111-1111-1111-1111-111111111111/default/")]
    [InlineData("/dav/addressbooks/11111111-1111-1111-1111-111111111111/de%66ault/")]
    public void AnHrefThatIsNotOurs_ResolvesToNothingRatherThanThrowing(string path) =>
        Assert.Null(DavPaths.Parse(path));

    [Fact]
    public void ANullHref_ResolvesToNothing() => Assert.Null(DavPaths.Parse(null));

    [Fact]
    public void ATraversalSpelledInFull_NeverEscapesTheCollection()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // A "/.." spelled with its slashes is a segment of its own: it makes the path one segment
        // too long and no resource of ours. Spelled inside the last segment, encoded or not, it
        // stays a name — one DavName refuses — never a path the store could follow.
        Assert.Null(DavPaths.Parse($"/dav/addressbooks/{userId}/default/../../../etc/passwd"));
        Assert.Null(DavPaths.Parse($"/dav/{userId}/../addressbooks/{userId}/default/a.vcf"));
        Assert.False(DavName.IsValid(DavPaths.Parse($"/dav/addressbooks/{userId}/default/..")!.DavName));
        Assert.False(DavName.IsValid(
            DavPaths.Parse($"/dav/addressbooks/{userId}/default/%2E%2E")!.DavName));
        Assert.False(DavName.IsValid(
            DavPaths.Parse($"/dav/addressbooks/{userId}/default/..%2F..%2Fetc%2Fpasswd")!.DavName));
        Assert.False(DavName.IsValid(
            DavPaths.Parse($"/dav/addressbooks/{userId}/default/%2E%2E%2Fetc")!.DavName));
    }

    [Fact]
    public void ADotSegmentThatSurvivesTheDecode_IsARefusedNameNotAParent()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // ".." reached the last segment percent-encoded, so nothing normalised it away: it comes
        // back as the two-character name that DavName refuses by name.
        Assert.Equal("..", DavPaths.Parse($"/dav/addressbooks/{userId}/default/%2E%2E")!.DavName);
    }

    [Fact]
    public void AnOverLongName_ComesBackWholeForDavNameToRefuse()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var name = new string('a', 256);

        var resource = DavPaths.Parse($"/dav/addressbooks/{userId}/default/{name}");

        // Parse never judges a name: the caller answers 404 on "not ours" and 403 on "not
        // acceptable", and those are two different answers.
        Assert.Equal(name, resource!.DavName);
        Assert.False(DavName.IsValid(resource.DavName));
    }

    [Fact]
    public void AnHrefLongerThanAnyOfOurs_ResolvesToNothing()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        Assert.Null(DavPaths.Parse(
            $"/dav/addressbooks/{userId}/default/{new string('a', 4096)}"));
    }

    [Theory]
    [InlineData("%")]
    [InlineData("%z")]
    [InlineData("%2")]
    [InlineData("%zz.vcf")]
    [InlineData("%C3.vcf")]
    [InlineData("%E0%A4.vcf")]
    [InlineData("%FF%FE")]
    public void AMalformedEscape_ComesBackAsItselfRatherThanThrowing(string segment)
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // Uri.UnescapeDataString leaves a sequence it cannot read untouched. A name is client
        // input: the worst a broken escape may do is fail to match a stored row.
        var resource = DavPaths.Parse($"/dav/addressbooks/{userId}/default/{segment}");

        Assert.Equal(segment, resource!.DavName);
    }

    [Fact]
    public void ANameThatIsLegalButNotACard_StillResolves()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // The ".vcf" suffix is a client convention: a name without it is still a card href, and
        // whether such a card exists is the store's answer, not this one's.
        var resource = DavPaths.Parse($"/dav/addressbooks/{userId}/default/notes.txt");

        Assert.Equal(DavResourceKind.Card, resource!.Kind);
        Assert.Equal("notes.txt", resource.DavName);
        Assert.Equal(userId, resource.UserId);
    }

    [Fact]
    public void APlusSign_IsNotASpace()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // '+' means a space in a form body, never in a path segment: reading it as one would make
        // two distinct card names collide.
        Assert.Equal("a+b.vcf", DavPaths.Parse($"/dav/addressbooks/{userId}/default/a+b.vcf")!.DavName);
    }

    [Fact]
    public void EachBuilderNestsInsideTheOneAbove()
    {
        Assert.StartsWith(DavPaths.Root + "/", DavPaths.Principal(User));
        Assert.StartsWith(DavPaths.Home(User), DavPaths.Collection(User));
        Assert.StartsWith(DavPaths.Collection(User), DavPaths.Card(User, "a.vcf"));
        Assert.EndsWith("/" + DavPaths.BookName + "/", DavPaths.Collection(User));
    }
}
