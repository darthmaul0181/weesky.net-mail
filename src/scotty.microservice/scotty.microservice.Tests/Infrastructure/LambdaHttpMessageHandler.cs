namespace weesky.Scotty.Microservice.Tests.Infrastructure;

/// <summary>Answers each request with whatever the test's delegate does: a stall, a throw, a count.</summary>
internal sealed class LambdaHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        send(request, cancellationToken);
}
