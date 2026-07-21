using MimeKit;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class AuthenticationResultsTests
{
    private static HeaderList Headers(params string[] values)
    {
        var headers = new HeaderList();
        foreach (var value in values) headers.Add(new Header("Authentication-Results", value));
        return headers;
    }

    [Fact]
    public void Parse_ReadsBothVerdictsFromARealHeader()
    {
        var headers = Headers(
            "mx.google.com; dkim=pass header.i=@claude.com header.s=s1; " +
            "spf=pass (google.com: domain of no-reply@claude.com designates 1.2.3.4 as permitted sender) " +
            "smtp.mailfrom=no-reply@claude.com; dmarc=pass header.from=claude.com");

        var result = AuthenticationResults.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal("pass", result!.Spf);
        Assert.Equal("pass", result.Dkim);
        Assert.Contains("dmarc=pass", result.Raw);
    }

    [Fact]
    public void Parse_ReturnsNullWithoutTheHeader()
    {
        var headers = new HeaderList { new Header("Subject", "hello") };

        Assert.Null(AuthenticationResults.Parse(headers));
    }

    // Each relay prepends its own header, so the topmost one is the receiving server's verdict.
    [Fact]
    public void Parse_LetsTheMostRecentHeaderWin()
    {
        var headers = Headers("mx.weesky.net; spf=fail; dkim=fail", "relay.upstream.net; spf=pass; dkim=pass");

        var result = AuthenticationResults.Parse(headers);

        Assert.Equal("fail", result!.Spf);
        Assert.Equal("fail", result.Dkim);
    }

    [Fact]
    public void Parse_FillsAMethodMissingFromTheFirstHeaderFromTheNext()
    {
        var headers = Headers("mx.weesky.net; spf=pass", "relay.upstream.net; dkim=pass");

        var result = AuthenticationResults.Parse(headers);

        Assert.Equal("pass", result!.Spf);
        Assert.Equal("pass", result.Dkim);
    }

    [Fact]
    public void Parse_LeavesAMissingMethodNull()
    {
        var result = AuthenticationResults.Parse(Headers("mx.weesky.net; spf=softfail"));

        Assert.Equal("softfail", result!.Spf);
        Assert.Null(result.Dkim);
    }

    [Fact]
    public void Parse_MatchesTheMethodAndNormalisesTheResultRegardlessOfCase()
    {
        var result = AuthenticationResults.Parse(Headers("mx.weesky.net; SPF=Pass; DKIM=PASS"));

        Assert.Equal("pass", result!.Spf);
        Assert.Equal("pass", result.Dkim);
    }

    // A header mentioning neither method still proves the server ran checks; the verdicts are
    // simply unknown, which the reader renders as no badge at all.
    [Fact]
    public void Parse_KeepsAHeaderCarryingNeitherMethod()
    {
        var result = AuthenticationResults.Parse(Headers("mx.weesky.net; dmarc=pass header.from=claude.com"));

        Assert.NotNull(result);
        Assert.Null(result!.Spf);
        Assert.Null(result.Dkim);
        Assert.Contains("dmarc=pass", result.Raw);
    }

    // "smtp.mailfrom=x" must not be mistaken for the spf method, nor "header.i=" for dkim.
    [Fact]
    public void Parse_IgnoresPropertiesThatMerelyContainTheMethodName()
    {
        var result = AuthenticationResults.Parse(Headers("mx.weesky.net; none; smtp.mailfrom=spf@x.be"));

        Assert.Null(result!.Spf);
        Assert.Null(result.Dkim);
    }
}
