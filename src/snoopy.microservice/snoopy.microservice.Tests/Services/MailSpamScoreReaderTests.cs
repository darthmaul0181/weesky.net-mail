using MimeKit;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class MailSpamScoreReaderTests
{
    private static HeaderList Headers(params (string Name, string Value)[] entries)
    {
        var headers = new HeaderList();
        foreach (var (name, value) in entries) headers.Add(new Header(name, value));
        return headers;
    }

    [Fact]
    public void Parse_ReadsAnRspamdResult()
    {
        var result = MailSpamScoreReader.Parse(Headers(
            ("X-Spamd-Result", "default: False [7.00 / 16.00]; R_SPF_ALLOW(-0.20)[+ip4:1.2.3.0/24]; DMARC_POLICY_ALLOW(-0.50)[weesky.be,none]")));

        Assert.NotNull(result);
        Assert.Equal(7.00, result!.Score);
        Assert.Equal(16.00, result.Threshold);
        Assert.StartsWith("X-Spamd-Result:", result.Raw);
    }

    [Fact]
    public void Parse_ReadsASpamAssassinStatus()
    {
        var result = MailSpamScoreReader.Parse(Headers(
            ("X-Spam-Status", "No, score=2.3 required=5.0 tests=DKIM_SIGNED,DKIM_VALID autolearn=ham version=4.0.0")));

        Assert.Equal(2.3, result!.Score);
        Assert.Equal(5.0, result.Threshold);
    }

    // X-Spam-Score alone carries no threshold; 5.0 is SpamAssassin's universal default.
    [Fact]
    public void Parse_FallsBackToABareSpamAssassinScore()
    {
        var result = MailSpamScoreReader.Parse(Headers(("X-Spam-Score", "8.2")));

        Assert.Equal(8.2, result!.Score);
        Assert.Equal(5.0, result.Threshold);
        Assert.StartsWith("X-Spam-Score:", result.Raw);
    }

    [Fact]
    public void Parse_ReadsAnExchangeScl()
    {
        var result = MailSpamScoreReader.Parse(Headers(("X-MS-Exchange-Organization-SCL", "6")));

        Assert.Equal(6, result!.Score);
        Assert.Equal(5, result.Threshold);
    }

    // SCL -1 marks trusted internal mail; a negative score would read as "less than clean".
    [Fact]
    public void Parse_TreatsTrustedInternalMailAsClean()
    {
        var result = MailSpamScoreReader.Parse(Headers(("X-MS-Exchange-Organization-SCL", "-1")));

        Assert.Equal(0, result!.Score);
    }

    // Our own platform runs rspamd, so its header beats whatever an upstream relay added.
    [Fact]
    public void Parse_PrefersRspamdOverTheOtherEngines()
    {
        var result = MailSpamScoreReader.Parse(Headers(
            ("X-Spam-Status", "Yes, score=9.9 required=5.0"),
            ("X-MS-Exchange-Organization-SCL", "9"),
            ("X-Spamd-Result", "default: False [1.10 / 15.00];")));

        Assert.Equal(1.10, result!.Score);
        Assert.Equal(15.00, result.Threshold);
    }

    [Fact]
    public void Parse_PrefersSpamAssassinOverScl()
    {
        var result = MailSpamScoreReader.Parse(Headers(
            ("X-MS-Exchange-Organization-SCL", "9"),
            ("X-Spam-Status", "No, score=1.5 required=5.0")));

        Assert.Equal(1.5, result!.Score);
    }

    [Fact]
    public void Parse_ReadsOnlyTheTopmostHeaderOfAName()
    {
        var result = MailSpamScoreReader.Parse(Headers(
            ("X-Spamd-Result", "default: False [7.00 / 16.00];"),
            ("X-Spamd-Result", "default: False [0.00 / 16.00];")));

        Assert.Equal(7.00, result!.Score);
    }

    // An unreadable header moves to the next ENGINE, never to a lower occurrence of the same name.
    [Fact]
    public void Parse_MovesToTheNextEngineWhenAHeaderIsUnreadable()
    {
        var result = MailSpamScoreReader.Parse(Headers(
            ("X-Spamd-Result", "default: False [garbled];"),
            ("X-Spam-Status", "No, score=2.3 required=5.0")));

        Assert.Equal(2.3, result!.Score);
        Assert.StartsWith("X-Spam-Status:", result.Raw);
    }

    [Fact]
    public void Parse_KeepsANegativeScore()
    {
        var result = MailSpamScoreReader.Parse(Headers(
            ("X-Spamd-Result", "default: False [-1.50 / 15.00];")));

        Assert.Equal(-1.50, result!.Score);
    }

    [Fact]
    public void Parse_ReturnsNullWithoutAnyKnownHeader()
    {
        Assert.Null(MailSpamScoreReader.Parse(Headers(("Subject", "hello"))));
    }

    [Fact]
    public void Parse_ReturnsNullWhenNothingIsReadable()
    {
        Assert.Null(MailSpamScoreReader.Parse(Headers(
            ("X-Spamd-Result", "nonsense"),
            ("X-Spam-Score", "not a number"),
            ("X-MS-Exchange-Organization-SCL", "high"))));
    }
}
