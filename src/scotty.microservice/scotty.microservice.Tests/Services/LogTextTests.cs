using weesky.Scotty.Microservice.Services;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services;

public sealed class LogTextTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("abc-123", "abc-123")]
    [InlineData("line\r\nINJECTED: x\tend", "line??INJECTED: x?end")]
    [InlineData("a\u2028b\u2029c", "a?b?c")]
    [InlineData("bell[0m", "?bell?[0m")]
    public void Safe_ReplacesEveryControlCharacter(string? value, string expected) =>
        Assert.Equal(expected, LogText.Safe(value));

    [Fact]
    public void Safe_TruncatesWithAnEllipsis()
    {
        var text = LogText.Safe(new string('a', 300));
        Assert.Equal(201, text.Length);
        Assert.EndsWith("…", text);
    }
}
