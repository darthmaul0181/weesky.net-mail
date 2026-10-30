namespace weesky.Scotty.Microservice.Tests.Infrastructure;

/// <summary>Hands out clients over one handler, whatever the name asked for.</summary>
internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
