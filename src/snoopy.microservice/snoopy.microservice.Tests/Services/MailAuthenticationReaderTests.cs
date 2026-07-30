using MimeKit;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class MailAuthenticationReaderTests
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
        const string header =
            "mx.google.com; dkim=pass header.i=@claude.com header.s=s1; " +
            "spf=pass (google.com: domain of no-reply@claude.com designates 1.2.3.4 as permitted sender) " +
            "smtp.mailfrom=no-reply@claude.com; dmarc=pass header.from=claude.com";

        var result = MailAuthenticationReader.Parse(Headers(header));

        Assert.NotNull(result);
        Assert.Equal("pass", result!.Spf);
        Assert.Equal("pass", result.Dkim);
        Assert.Equal("pass", result.Dmarc);
        Assert.Equal(header, result.Raw);
    }

    [Fact]
    public void Parse_ReadsTheDmarcVerdict()
    {
        const string header = "mx.google.com; spf=pass smtp.mailfrom=a@b.test; " +
                              "dkim=pass header.i=@b.test; dmarc=fail header.from=b.test";

        var result = MailAuthenticationReader.Parse(Headers(header));

        Assert.Equal("fail", result!.Dmarc);
    }

    [Fact]
    public void Parse_LeavesDmarcNullWhenTheHeaderCarriesNone()
    {
        var result = MailAuthenticationReader.Parse(Headers("mx.google.com; spf=pass smtp.mailfrom=a@b.test"));

        Assert.Null(result!.Dmarc);
    }

    [Fact]
    public void Parse_ReturnsNullWithoutTheHeader()
    {
        var headers = new HeaderList { new Header("Subject", "hello") };

        Assert.Null(MailAuthenticationReader.Parse(headers));
    }

    // Each relay prepends its own header, so the topmost one is the receiving server's verdict.
    [Fact]
    public void Parse_LetsTheTopmostHeaderWin()
    {
        var headers = Headers("mx.weesky.net; spf=fail; dkim=fail", "relay.upstream.net; spf=pass; dkim=pass");

        var result = MailAuthenticationReader.Parse(headers);

        Assert.Equal("fail", result!.Spf);
        Assert.Equal("fail", result.Dkim);
    }

    // Every header below the topmost was written by an untrusted relay (or forged by the
    // sender), so a verdict missing from the topmost header must stay null, never borrowed.
    [Fact]
    public void Parse_DoesNotFillAMissingMethodFromALaterHeader()
    {
        var headers = Headers("mx.weesky.net; spf=pass", "relay.upstream.net; dkim=pass");

        var result = MailAuthenticationReader.Parse(headers);

        Assert.Equal("pass", result!.Spf);
        Assert.Null(result.Dkim);
    }

    [Fact]
    public void Parse_LeavesAMissingMethodNull()
    {
        var result = MailAuthenticationReader.Parse(Headers("mx.weesky.net; spf=softfail"));

        Assert.Equal("softfail", result!.Spf);
        Assert.Null(result.Dkim);
    }

    [Fact]
    public void Parse_MatchesTheMethodAndNormalisesTheResultRegardlessOfCase()
    {
        var result = MailAuthenticationReader.Parse(Headers("mx.weesky.net; SPF=Pass; DKIM=PASS"));

        Assert.Equal("pass", result!.Spf);
        Assert.Equal("pass", result.Dkim);
    }

    // A header mentioning neither method still proves the server ran checks; the verdicts are
    // simply unknown, which the reader renders as no badge at all.
    [Fact]
    public void Parse_KeepsAHeaderCarryingNeitherMethod()
    {
        const string header = "mx.weesky.net; dmarc=pass header.from=claude.com";

        var result = MailAuthenticationReader.Parse(Headers(header));

        Assert.NotNull(result);
        Assert.Null(result!.Spf);
        Assert.Null(result.Dkim);
        Assert.Equal(header, result.Raw);
    }

    // "smtp.mailfrom=" without a preceding "spf=" is not valid RFC 7601 grammar (a ptype.pname
    // needs a method before it), so the parse fails outright. The fallback keeps the raw header.
    [Fact]
    public void Parse_KeepsAnUnparsableHeaderAndReportsNoVerdicts()
    {
        const string header = "mx.weesky.net; none; smtp.mailfrom=spf@x.be";

        var result = MailAuthenticationReader.Parse(Headers(header));

        Assert.NotNull(result);
        Assert.Null(result!.Spf);
        Assert.Null(result.Dkim);
        Assert.Equal(header, result.Raw);
    }

    // A method's name appearing inside a property value must not be misread as a verdict.
    [Fact]
    public void Parse_DoesNotMistakeAMethodNameInsideAPropertyForAVerdict()
    {
        var result = MailAuthenticationReader.Parse(
            Headers("mx.weesky.net; dkim=pass header.i=@spf.example.com"));

        Assert.Equal("pass", result!.Dkim);
        Assert.Null(result.Spf);
    }

    // A naive semicolon-split parser would extract `spf=pass` from the comment, misreading it
    // as a verdict. MimeKit correctly treats the whole `(note; spf=pass)` as a comment, so the
    // real `spf=softfail` that follows is the verdict.
    [Fact]
    public void Parse_DoesNotMistakeAVerdictLikeTextInsideACommentForAVerdict()
    {
        var result = MailAuthenticationReader.Parse(
            Headers("mx.weesky.net; dkim=fail (note; spf=pass); spf=softfail"));

        Assert.Equal("fail", result!.Dkim);
        Assert.Equal("softfail", result.Spf);
    }

    // Two DKIM signatures can legitimately disagree (e.g. a mailing list breaks the original
    // one while its own verifies); any passing occurrence makes the method pass.
    [Fact]
    public void Parse_TreatsAMethodAsPassingIfAnyOccurrencePasses()
    {
        var result = MailAuthenticationReader.Parse(
            Headers("mx.weesky.net; dkim=fail header.i=@a.com; dkim=pass header.i=@b.com"));

        Assert.Equal("pass", result!.Dkim);
    }
}
