using weesky.Snoopy.Microservice.Services.CardDav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CardDav;

public sealed class EntityTagMatcherTests
{
    [Theory]
    [InlineData("\"abc\"", "\"abc\"", true)]
    [InlineData("*", "\"abc\"", true)]
    [InlineData("W/\"abc\"", "\"abc\"", true)]
    [InlineData("\"xyz\", \"abc\"", "\"abc\"", true)]
    [InlineData("\"xyz\" , W/\"abc\"", "\"abc\"", true)]
    [InlineData("\"xyz\"", "\"abc\"", false)]
    [InlineData("", "\"abc\"", false)]
    [InlineData(null, "\"abc\"", false)]
    public void NoneMatch_UsesTheWeakComparison(string? header, string tag, bool expected) =>
        Assert.Equal(expected, EntityTagMatcher.NoneMatch(header, tag));

    [Theory]
    [InlineData("\"abc\"", "\"abc\"", true)]
    [InlineData("*", "\"abc\"", true)]
    [InlineData("\"xyz\", \"abc\"", "\"abc\"", true)]
    [InlineData("W/\"abc\"", "\"abc\"", false)]
    [InlineData("\"xyz\"", "\"abc\"", false)]
    [InlineData(null, "\"abc\"", false)]
    public void Match_UsesTheStrongComparison(string? header, string tag, bool expected) =>
        // If-Match guards a write. A weak tag says "semantically equivalent", which is not a
        // promise the byte-for-byte replacement of a card can rest on.
        Assert.Equal(expected, EntityTagMatcher.Match(header, tag));

    [Fact]
    public void AMalformedHeader_MatchesNothingRatherThanThrowing()
    {
        // A header is client input. The worst it may do is fail to match; a throw here would be a
        // 500 on a conditional GET, which a DAV client retries for ever.
        Assert.False(EntityTagMatcher.NoneMatch("not a tag at all", "\"abc\""));
        Assert.False(EntityTagMatcher.Match("\"unterminated", "\"abc\""));
    }
}
