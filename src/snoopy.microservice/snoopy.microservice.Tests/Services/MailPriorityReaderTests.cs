using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class MailPriorityReaderTests
{
    private static HeaderList Headers(params (string Field, string Value)[] entries)
    {
        var headers = new HeaderList();
        foreach (var (field, value) in entries) headers.Add(field, value);
        return headers;
    }

    [Theory]
    [InlineData("1", MailPriority.High)]
    [InlineData("1 (Highest)", MailPriority.High)]
    [InlineData("2 (High)", MailPriority.High)]
    [InlineData("3", MailPriority.Normal)]
    [InlineData("3 (Normal)", MailPriority.Normal)]
    [InlineData("4 (Low)", MailPriority.Low)]
    [InlineData("5 (Lowest)", MailPriority.Low)]
    public void ReadsTheLevelOutOfXPriorityPastItsComment(string value, MailPriority expected) =>
        Assert.Equal(expected, MailPriorityReader.Parse(Headers(("X-Priority", value))));

    /// <summary>An explicit 3 is an explicit Normal — going on to Importance would overrule the sender.</summary>
    [Fact]
    public void AnExplicitNormalStopsTheChain() =>
        Assert.Equal(MailPriority.Normal, MailPriorityReader.Parse(
            Headers(("X-Priority", "3"), ("Importance", "high"))));

    [Fact]
    public void AnUnreadableXPriorityFallsThroughToImportance() =>
        Assert.Equal(MailPriority.High, MailPriorityReader.Parse(
            Headers(("X-Priority", "urgent"), ("Importance", "high"))));

    [Fact]
    public void AnOutOfRangeXPriorityFallsThrough() =>
        Assert.Equal(MailPriority.Low, MailPriorityReader.Parse(
            Headers(("X-Priority", "9"), ("Importance", "low"))));

    [Theory]
    [InlineData("Importance", "high", MailPriority.High)]
    [InlineData("Importance", "LOW", MailPriority.Low)]
    [InlineData("Importance", "normal", MailPriority.Normal)]
    [InlineData("X-MSMail-Priority", "High", MailPriority.High)]
    [InlineData("X-MSMail-Priority", "Low", MailPriority.Low)]
    [InlineData("Priority", "urgent", MailPriority.High)]
    [InlineData("Priority", "non-urgent", MailPriority.Low)]
    public void ReadsTheWordHeaders(string field, string value, MailPriority expected) =>
        Assert.Equal(expected, MailPriorityReader.Parse(Headers((field, value))));

    /// <summary>The rule every header reader here follows — everything below the top could be forged.</summary>
    [Fact]
    public void TheTopmostOccurrenceWins() =>
        Assert.Equal(MailPriority.High, MailPriorityReader.Parse(
            Headers(("X-Priority", "1"), ("X-Priority", "5"))));

    [Fact]
    public void NoHeaderAtAllIsNormal() => Assert.Equal(MailPriority.Normal, MailPriorityReader.Parse(Headers()));

    [Fact]
    public void AnUnreadableValueEverywhereIsNormal() =>
        Assert.Equal(MailPriority.Normal, MailPriorityReader.Parse(
            Headers(("X-Priority", "banana"), ("Importance", "very"))));

    [Fact]
    public void FieldsNamesTheFourHeadersAFetchMustRequest() =>
        Assert.Equal(["X-Priority", "Importance", "X-MSMail-Priority", "Priority"], MailPriorityReader.Fields);
}
