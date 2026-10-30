namespace weesky.Scotty.Microservice.Tests.Infrastructure;

/// <summary>Hands out clients over one handler, whatever the name asked for, and records the names.</summary>
internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public List<string> Names { get; } = [];

    public HttpClient CreateClient(string name)
    {
        lock (Names) Names.Add(name);
        return new(handler, disposeHandler: false);
    }
}
