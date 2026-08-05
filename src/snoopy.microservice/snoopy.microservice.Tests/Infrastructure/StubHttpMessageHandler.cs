using System.Net;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>Answers a queued script of responses and records what was asked.</summary>
internal sealed class StubHttpMessageHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
{
    private int _served;

    public List<string> Bodies { get; } = [];

    public int Calls => _served;

    public static Func<HttpResponseMessage> Json(HttpStatusCode status, string body) =>
        () => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Bodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        var index = Interlocked.Increment(ref _served) - 1;
        if (index >= responses.Length) throw new InvalidOperationException("No scripted response left.");
        return responses[index]();
    }
}
