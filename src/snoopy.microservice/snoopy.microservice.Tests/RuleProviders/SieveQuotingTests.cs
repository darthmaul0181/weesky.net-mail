using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.RuleProviders;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.RuleProviders;

public sealed class SieveQuotingTests
{
    [Fact]
    public void Quote_WrapsPlainStringInDoubleQuotes()
        => Assert.Equal("\"INBOX\"", SieveQuoting.Quote("INBOX"));

    [Fact]
    public void Quote_EscapesEmbeddedDoubleQuote()
        => Assert.Equal("\"a\\\"b\"", SieveQuoting.Quote("a\"b"));

    [Fact]
    public void Quote_EscapesEmbeddedBackslash()
        => Assert.Equal("\"\\\\Seen\"", SieveQuoting.Quote("\\Seen"));

    [Fact]
    public void Quote_EmptyString_ReturnsEmptyQuotes()
        => Assert.Equal("\"\"", SieveQuoting.Quote(""));

    // Quoting escapes " and \ — everything a Sieve quoted-string needs — so a CRLF travels through
    // it intact and lands in the compiled script as a real line break the rule editor never shows.
    // ManageSieveSession refuses the same characters in a script name for the same reason.

    private static SieveRule Rule(string name = "Newsletters", string value = "news@example.com",
        string? headerName = null, string argument = "Archive") => new()
    {
        Name = name,
        Enabled = true,
        Conditions = [new SieveCondition
        {
            Field = headerName is null ? SieveConditionField.From : SieveConditionField.Header,
            HeaderName = headerName,
            Operator = SieveConditionOperator.Contains,
            Value = value,
        }],
        Actions = [new SieveAction { Type = SieveActionType.FileInto, Argument = argument }],
    };

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\t")]
    [InlineData("\0")]
    public void RejectControlCharacters_RefusesOneInAConditionValue(string control)
        => Assert.True(SieveQuoting.RejectControlCharacters(Rule(value: $"a{control}b")).IsFailure);

    [Fact]
    public void RejectControlCharacters_RefusesOneInTheRuleName()
        => Assert.True(SieveQuoting.RejectControlCharacters(Rule(name: "News\r\nletters")).IsFailure);

    [Fact]
    public void RejectControlCharacters_RefusesOneInACustomHeaderName()
        => Assert.True(SieveQuoting.RejectControlCharacters(Rule(headerName: "X-Spam\r\nFrom")).IsFailure);

    [Fact]
    public void RejectControlCharacters_RefusesOneInAnActionArgument()
        => Assert.True(SieveQuoting.RejectControlCharacters(Rule(argument: "Archive\nINBOX")).IsFailure);

    [Fact]
    public void RejectControlCharacters_AcceptsAnOrdinaryRule()
        => Assert.True(SieveQuoting.RejectControlCharacters(Rule()).IsSuccess);

    // Accents and quotes are ordinary content, not control characters: the guard must not
    // widen into a character allowlist.
    [Fact]
    public void RejectControlCharacters_AcceptsAccentsAndQuotes()
        => Assert.True(SieveQuoting.RejectControlCharacters(Rule(value: "aoû\"t@éxample.com")).IsSuccess);
}
