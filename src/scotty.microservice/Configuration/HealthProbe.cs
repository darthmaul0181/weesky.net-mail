using System.Net;

namespace weesky.Scotty.Microservice.Configuration;

/// <summary>
/// <c>scotty.microservice healthcheck</c>: the Docker image has no shell and no curl, so the
/// container's health check asks the running service's /health through the binary itself.
/// </summary>
internal static class HealthProbe
{
    internal static async Task<int> RunAsync(string? httpPorts, TextWriter error, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        var port = httpPorts?.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "8080";
        if (!ushort.TryParse(port, out _))
        {
            await error.WriteLineAsync($"Health check failed: ASPNETCORE_HTTP_PORTS starts with '{port}', which is not a port.");
            return 1;
        }

        var wait = timeout ?? TimeSpan.FromSeconds(5);
        using var client = new HttpClient(handler ?? new SocketsHttpHandler()) { Timeout = wait };
        try
        {
            using var response = await client.GetAsync($"http://127.0.0.1:{port}/health");
            if (response.StatusCode == HttpStatusCode.OK) return 0;
            await error.WriteLineAsync($"Health check failed: /health answered {(int)response.StatusCode}.");
        }
        catch (HttpRequestException e)
        {
            await error.WriteLineAsync($"Health check failed: {e.Message}");
        }
        catch (TaskCanceledException)
        {
            await error.WriteLineAsync($"Health check failed: no answer within {wait.TotalSeconds:0.###} s.");
        }

        return 1;
    }
}
