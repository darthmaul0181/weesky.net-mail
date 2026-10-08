using weesky.Scotty.Microservice.Models;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Models;

public sealed class BingArchiveTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    internal static string Archive(
        string urlBase = "/th?id=OHR.MayotteOctopus_FR-FR2063163267",
        string hash = "baeeb03654162a2eaa1439de2eee4b89",
        string start = "202610072200",
        string title = "Poulpe fiction") => $$"""
        {"images":[{"fullstartdate":"{{start}}","urlbase":"{{urlBase}}",
          "copyright":"Poulpe en position défensive, Mayotte (© Gabriel Barathieu/Minden Pictures)",
          "title":"{{title}}","hsh":"{{hash}}"}]}
        """;

    [Fact]
    public void Parse_ReadsTheEntryAndRebuildsTheImageUrl()
    {
        var entry = BingArchive.Parse(Archive(), Now).Value;

        Assert.Equal("baeeb03654162a2eaa1439de2eee4b89", entry.Version);
        Assert.Equal("Poulpe fiction", entry.Title);
        Assert.Equal("Poulpe en position défensive, Mayotte (© Gabriel Barathieu/Minden Pictures)", entry.Copyright);
        Assert.Equal(new Uri("https://www.bing.com/th?id=OHR.MayotteOctopus_FR-FR2063163267_1920x1080.jpg"), entry.ImageUrl);
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 22, 0, 0, TimeSpan.Zero), entry.ExpiresAt);
    }

    [Theory]
    [InlineData("//evil.test/th?id=x")]
    [InlineData("https://evil.test/th?id=x")]
    [InlineData("/th?id=x&url=https://evil.test")]
    [InlineData("/elsewhere?id=x")]
    public void Parse_RefusesAPathOutsideTheThumbnailRoute(string urlBase)
        => Assert.True(BingArchive.Parse(Archive(urlBase: urlBase), Now).IsFailure);

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"images":[]}""")]
    [InlineData("""{"images":["x"]}""")]
    [InlineData("""{"images":[{"urlbase":"/th?id=x","hsh":"abc","fullstartdate":"202610072200","copyright":"c"}]}""")]
    public void Parse_RefusesAnIncompleteArchive(string json)
        => Assert.True(BingArchive.Parse(json, Now).IsFailure);

    [Theory]
    [InlineData("2026-10-07")]
    [InlineData("")]
    public void Parse_RefusesAnUnreadableStartDate(string start)
        => Assert.True(BingArchive.Parse(Archive(start: start), Now).IsFailure);

    // The hash becomes a path segment of our own route.
    [Theory]
    [InlineData("../x")]
    [InlineData("")]
    public void Parse_RefusesAHashThatIsNotPlainAlphanumeric(string hash)
        => Assert.True(BingArchive.Parse(Archive(hash: hash), Now).IsFailure);

    [Fact]
    public void Parse_FloorsAStaleExpiry()
    {
        var entry = BingArchive.Parse(Archive(start: "202610042200"), Now).Value;

        Assert.Equal(Now.AddHours(1), entry.ExpiresAt);
    }

    [Theory]
    [InlineData("fr", "fr-FR")]
    [InlineData("en", "en-US")]
    [InlineData(null, "en-US")]
    [InlineData("de", "en-US")]
    public void MarketOf_FollowsTheInterfaceLanguage(string? lang, string market)
        => Assert.Equal(market, BingArchive.MarketOf(lang));
}
