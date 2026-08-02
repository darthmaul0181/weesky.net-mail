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
}
