using Microsoft.AspNetCore.Http;
using Serilog.Events;
using weesky.Scotty.Microservice.Configuration;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

/// <summary>One page load fetches a dozen files: served, they would bury the API's lines.</summary>
public sealed class RequestLogLevelTests
{
    [Theory]
    [InlineData("/health", 200, LogEventLevel.Verbose)]
    [InlineData("/health", 503, LogEventLevel.Verbose)]
    [InlineData("/mail/inbox", 200, LogEventLevel.Verbose)]
    [InlineData("/assets/app-abc123.js", 304, LogEventLevel.Verbose)]
    [InlineData("/api/Login", 200, LogEventLevel.Information)]
    [InlineData("/dav/calendars", 207, LogEventLevel.Information)]
    [InlineData("/wp-login.php", 404, LogEventLevel.Information)]
    public void APageServed_IsQuietAndEverythingElseIsLogged(string path, int status, LogEventLevel expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.StatusCode = status;

        Assert.Equal(expected, LoggingConfiguration.RequestLevel(context));
    }
}
