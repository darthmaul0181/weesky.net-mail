using System.Net;
using weesky.Scotty.Microservice.Configuration;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

public sealed class HealthProbeTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public Uri? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request.RequestUri;
            return send(request, cancellationToken);
        }
    }

    private static Handler Answering(HttpStatusCode status) => new((_, _) => Task.FromResult(new HttpResponseMessage(status)));

    [Fact]
    public async Task Healthy_ExitsZeroAndSaysNothing()
    {
        var error = new StringWriter();

        Assert.Equal(0, await HealthProbe.RunAsync("8080", error, Answering(HttpStatusCode.OK)));
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public async Task TheFirstConfiguredPort_IsTheOneAsked()
    {
        var handler = Answering(HttpStatusCode.OK);

        await HealthProbe.RunAsync("8081;8082", TextWriter.Null, handler);

        Assert.Equal(new Uri("http://127.0.0.1:8081/health"), handler.Asked);
    }

    [Fact]
    public async Task WithoutAPort_8080IsAsked()
    {
        var handler = Answering(HttpStatusCode.OK);

        await HealthProbe.RunAsync(null, TextWriter.Null, handler);

        Assert.Equal(new Uri("http://127.0.0.1:8080/health"), handler.Asked);
    }

    [Fact]
    public async Task Unhealthy_ExitsOneNamingTheStatus()
    {
        var error = new StringWriter();

        Assert.Equal(1, await HealthProbe.RunAsync("8080", error, Answering(HttpStatusCode.ServiceUnavailable)));
        Assert.Contains("503", error.ToString());
    }

    [Fact]
    public async Task ARefusedConnection_ExitsOne()
    {
        var error = new StringWriter();
        var handler = new Handler((_, _) => throw new HttpRequestException("Connection refused"));

        Assert.Equal(1, await HealthProbe.RunAsync("8080", error, handler));
        Assert.Contains("Connection refused", error.ToString());
    }

    [Fact]
    public async Task NoAnswerInTime_ExitsOne()
    {
        var error = new StringWriter();
        var handler = new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Assert.Equal(1, await HealthProbe.RunAsync("8080", error, handler, TimeSpan.FromMilliseconds(50)));
        Assert.Contains("no answer", error.ToString());
    }

    [Fact]
    public async Task AnUnreadablePort_ExitsOneNamingIt()
    {
        var error = new StringWriter();

        Assert.Equal(1, await HealthProbe.RunAsync("http", error, Answering(HttpStatusCode.OK)));
        Assert.Contains("'http'", error.ToString());
    }
}
