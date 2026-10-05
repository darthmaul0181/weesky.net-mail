using Serilog;
using Serilog.Core;
using weesky.Scotty.Microservice.Configuration;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

[Collection(nameof(LoggingConsoleCollection))]
public sealed class LoggingConfigurationTests : IDisposable
{
    private const string RequestSource = "Serilog.AspNetCore.RequestLoggingMiddleware";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"scotty-logs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData(null, "File")]
    [InlineData("", "File")]
    [InlineData("file", "File")]
    [InlineData("FILE", "File")]
    [InlineData("console", "Console")]
    [InlineData("Console", "Console")]
    public void ParseOutput_ReadsTheSetting(string? value, string expected)
    {
        Assert.Equal(expected, LoggingConfiguration.ParseOutput(value).ToString());
    }

    [Fact]
    public void ParseOutput_RefusesToStartOnAnUnknownValue()
    {
        var error = Assert.Throws<InvalidOperationException>(() => LoggingConfiguration.ParseOutput("stdout"));

        Assert.Contains("Logs:Output is \"stdout\"", error.Message);
        Assert.Contains("file, console", error.Message);
    }

    [Fact]
    public void Console_WritesBothStreamsWithTheSameFiltersAndCreatesNoFolder()
    {
        var original = Console.Out;
        var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            using (var logger = LoggingConfiguration
                       .Configure(new LoggerConfiguration(), LogOutput.Console, _directory, "").CreateLogger())
                Write(logger);
        }
        finally
        {
            Console.SetOut(original);
        }

        var text = output.ToString();
        Assert.Contains("HTTP GET /api/Mail from 203.0.113.7", text);
        Assert.Contains("Mailbox opened", text);
        Assert.DoesNotContain("Kestrel chatter", text);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void File_KeepsRequestsApartFromTheRest()
    {
        using (var logger = LoggingConfiguration
                   .Configure(new LoggerConfiguration(), LogOutput.File, _directory, "").CreateLogger())
            Write(logger);

        var http = File.ReadAllText(Assert.Single(Directory.GetFiles(_directory, "log-http-*.log")));
        var rest = File.ReadAllText(Assert.Single(Directory.GetFiles(_directory, "log-2*.log")));
        Assert.Contains("from 203.0.113.7", http);
        Assert.DoesNotContain("Mailbox opened", http);
        Assert.Contains("Mailbox opened", rest);
        Assert.DoesNotContain("Kestrel chatter", rest);
    }

    private static void Write(Logger logger)
    {
        logger.ForContext(Constants.SourceContextPropertyName, RequestSource)
            .Information("HTTP {RequestMethod} {RequestPath} from {ClientIp}", "GET", "/api/Mail", "203.0.113.7");
        logger.ForContext(Constants.SourceContextPropertyName, "weesky.Scotty.Mail").Information("Mailbox opened");
        logger.ForContext(Constants.SourceContextPropertyName, "Microsoft.AspNetCore.Server.Kestrel")
            .Information("Kestrel chatter");
    }
}
