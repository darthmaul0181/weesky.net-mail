using MimeKit;
using weesky.Scotty.Microservice.Services;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services;

public sealed class MailBimiHintTests
{
    private const string Pass = "mx.weesky.net; dmarc=pass header.from=news.example.com";

    private static HeaderList Headers(string? selector, string results = Pass)
    {
        var headers = new HeaderList { new Header("Authentication-Results", results) };
        if (selector is not null) headers.Add(new Header("BIMI-Selector", selector));
        return headers;
    }

    [Fact]
    public void Parse_ReadsSelectorFromBimiSelectorHeader() =>
        Assert.Equal(("news.example.com", "news"), MailBimiHint.Parse(Headers("v=BIMI1; s=news;")));

    [Fact]
    public void Parse_DottedSelector_IsKept() =>
        Assert.Equal(("news.example.com", "v2.news"), MailBimiHint.Parse(Headers("v=BIMI1; s=v2.news")));

    [Theory]
    [InlineData("v=BIMI1; s=..")]
    [InlineData("v=BIMI1; s=../x")]
    [InlineData("v=BIMI1; s=")]
    [InlineData("v=BIMI1")]
    [InlineData(null)]
    public void Parse_BadSelector_FallsBackToNull(string? selector) =>
        Assert.Equal(("news.example.com", (string?)null), MailBimiHint.Parse(Headers(selector)));

    [Fact]
    public void Parse_NoDmarcPass_ReturnsNullDomainAndNullSelector() =>
        Assert.Equal(((string?)null, (string?)null),
            MailBimiHint.Parse(Headers("v=BIMI1; s=news;", "mx.weesky.net; dmarc=fail header.from=news.example.com")));

    [Fact]
    public void Fields_ContainsAuthenticationResultsAndBimiSelectorAndPriorityFields()
    {
        Assert.Contains("Authentication-Results", MailBimiHint.Fields);
        Assert.Contains("BIMI-Selector", MailBimiHint.Fields);
        Assert.All(MailPriorityReader.Fields, f => Assert.Contains(f, MailBimiHint.Fields));
    }
}
