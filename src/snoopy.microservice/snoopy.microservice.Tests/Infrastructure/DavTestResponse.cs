namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// One /dav response, fully buffered: the integer status a DAV test asserts on, the body, and the
/// response + content headers flattened into one lookup (ETag, Allow, DAV, Content-Type...).
/// </summary>
internal sealed class DavTestResponse
{
    private DavTestResponse(int statusCode, string body, IReadOnlyDictionary<string, string> headers)
    {
        StatusCode = statusCode;
        Body = body;
        Headers = headers;
    }

    internal int StatusCode { get; }

    internal string Body { get; }

    internal IReadOnlyDictionary<string, string> Headers { get; }

    internal string? Header(string name) =>
        Headers.TryGetValue(name, out var value) ? value : null;

    /// <summary>Kept as a Task for the tests written against a streaming read.</summary>
    internal Task<string> ReadAsync() => Task.FromResult(Body);

    internal static async Task<DavTestResponse> ReadAsync(HttpResponseMessage message)
    {
        var body = await message.Content.ReadAsStringAsync();
        var headers = message.Headers.Concat(message.Content.Headers).ToDictionary(
            pair => pair.Key, pair => string.Join(", ", pair.Value), StringComparer.OrdinalIgnoreCase);

        return new DavTestResponse((int)message.StatusCode, body, headers);
    }
}
