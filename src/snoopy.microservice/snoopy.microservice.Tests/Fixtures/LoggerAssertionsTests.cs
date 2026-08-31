using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Fixtures;

/// <summary>
/// The assertions of <see cref="LoggerAssertions"/>, proved able to FAIL. A false test helper is
/// worse than no helper, because it looks like proof — and this file exists because an earlier
/// slice of this project shipped one whose every <c>Times.Never</c> assertion was vacuous, passing
/// whatever the logger had actually recorded.
/// </summary>
public sealed class LoggerAssertionsTests
{
    [Fact]
    public void WithAll_PassesOnALineCarryingEveryText()
    {
        var logger = Logged(LogLevel.Information, "dav {Method} status={Status}", "REPORT", 207);

        logger.VerifyInformationLoggedWithAll("REPORT", "status=207");
    }

    [Fact]
    public void WithAll_FailsWhenOneOfTheTextsIsMissing()
    {
        var logger = Logged(LogLevel.Information, "dav {Method} status={Status}", "REPORT", 207);

        Assert.Throws<MockException>(() =>
            logger.VerifyInformationLoggedWithAll("REPORT", "status=403"));
    }

    [Fact]
    public void WithAll_FailsWhenTheTextsAreSpreadOverSeveralLines()
    {
        var logger = new Mock<ILogger<LoggerAssertionsTests>>();
        logger.Object.LogInformation("dav {Method}", "REPORT");
        logger.Object.LogInformation("dav status={Status}", 207);

        // A line is read whole: an assertion satisfied by two half-lines proves nothing about the
        // one line an operator would actually be reading.
        Assert.Throws<MockException>(() =>
            logger.VerifyInformationLoggedWithAll("REPORT", "status=207"));
    }

    [Fact]
    public void WithAll_FailsWhenNothingWasLoggedAtAll()
    {
        Assert.Throws<MockException>(() =>
            new Mock<ILogger<LoggerAssertionsTests>>().VerifyInformationLoggedWithAll("REPORT"));
    }

    [Fact]
    public void WithAll_FailsWhenTheLineWasLoggedBelowInformation()
    {
        var logger = Logged(LogLevel.Debug, "dav {Method}", "REPORT");

        Assert.Throws<MockException>(() => logger.VerifyInformationLoggedWithAll("REPORT"));
    }

    [Fact]
    public void NoValueContains_PassesWhenTheTextIsNowhere()
    {
        var logger = Logged(LogLevel.Information, "dav {Resource}", "/dav/addressbooks/1/default/");

        logger.VerifyNoLoggedValueContains("@");
    }

    [Fact]
    public void NoValueContains_FailsWhenAValueCarriesIt()
    {
        var logger = Logged(LogLevel.Information, "dav {User}", "someone@weesky.be");

        Assert.Throws<MockException>(() => logger.VerifyNoLoggedValueContains("@"));
    }

    [Fact]
    public void NoValueContains_FailsWhenTheTemplateItselfCarriesIt()
    {
        var logger = Logged(LogLevel.Information, "dav someone@weesky.be");

        // The template travels as the {OriginalFormat} value: a secret hard-coded into it would
        // otherwise slip past an assertion that only reads the substituted arguments.
        Assert.Throws<MockException>(() => logger.VerifyNoLoggedValueContains("@"));
    }

    [Fact]
    public void NoValueContains_FailsWhenTheLeakIsAtAnotherLevel()
    {
        var logger = Logged(LogLevel.Warning, "dav {User}", "someone@weesky.be");

        // A secret leaked at Warning is leaked.
        Assert.Throws<MockException>(() => logger.VerifyNoLoggedValueContains("@"));
    }

    [Fact]
    public void SingleTemplate_ReadsTheTemplateAndNotTheRenderedLine()
    {
        var logger = Logged(LogLevel.Information, "dav {Method} status={Status}", "REPORT", 207);

        Assert.Equal("dav {Method} status={Status}", logger.SingleTemplate());
    }

    [Fact]
    public void SingleTemplate_RefusesToGuessWhenSeveralLinesWereLogged()
    {
        var logger = Logged(LogLevel.Information, "first");
        logger.Object.LogInformation("second");

        Assert.Throws<InvalidOperationException>(() => logger.SingleTemplate());
    }

    private static Mock<ILogger<LoggerAssertionsTests>> Logged(
        LogLevel level, string template, params object?[] arguments)
    {
        var logger = new Mock<ILogger<LoggerAssertionsTests>>();
        logger.Object.Log(level, template, arguments);
        return logger;
    }
}
