using System.Collections.Frozen;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Microservice.Services;

internal sealed class GravatarService(
    IHttpClientFactory clients, TimeProvider clock, ILogger<GravatarService> logger) : IAvatarService, IDisposable
{
    public const string ClientName = "gravatar";
    public const int MaxBytes = 64 * 1024;
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan HitLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan MissLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan FailureLifetime = TimeSpan.FromMinutes(5);
    private const long CacheBytes = 32 * 1024 * 1024;

    // Never SVG: a picture that can carry script is not a picture.
    private static readonly FrozenSet<string> ImageTypes =
        FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "image/jpeg", "image/png", "image/gif", "image/webp");

    private readonly AnswerCache cache = new(clock, CacheBytes);

    public Task<AvatarImage?> GetAsync(string address, CancellationToken cancellationToken)
    {
        var hash = HashOf(address);
        return cache.GetAsync(hash, () => FetchAsync(hash), cancellationToken);
    }

    public void Dispose() => cache.Dispose();

    internal static string HashOf(string address) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(address.Trim().ToLowerInvariant())));

    private async Task<(AvatarImage? Image, TimeSpan Lifetime)> FetchAsync(string hash)
    {
        // HttpClient.Timeout stops at the headers: a body dripped by the upstream would hold every waiter.
        using var budget = new CancellationTokenSource(FetchTimeout, clock);
        try
        {
            using var response = await clients.CreateClient(ClientName).GetAsync(
                $"https://gravatar.com/avatar/{hash}?s=80&d=404", HttpCompletionOption.ResponseHeadersRead, budget.Token);
            var status = response.StatusCode;
            if (status == HttpStatusCode.NotFound) return (null, MissLifetime);
            if (status == HttpStatusCode.TooManyRequests || (int)status >= 500) return Failed(hash, status);
            if (status != HttpStatusCode.OK) return Refused(hash, "status", status);

            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is null || !ImageTypes.Contains(type)) return Refused(hash, "content type", type);
            var bytes = await response.Content.ReadCappedAsync(MaxBytes, budget.Token);
            if (bytes is null or []) return Refused(hash, "body", bytes is null ? "over the size cap" : "empty");

            return (new AvatarImage(bytes, type.ToLowerInvariant()), HitLifetime);
        }
        // No visitor's token reaches here: a cancellation is the budget or the client's own timeout.
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Gravatar unavailable for {Hash}", hash);
            return (null, FailureLifetime);
        }
    }

    private (AvatarImage?, TimeSpan) Failed(string hash, HttpStatusCode status)
    {
        logger.LogWarning("Gravatar unavailable for {Hash}: answered {Status}", hash, status);
        return (null, FailureLifetime);
    }

    private (AvatarImage?, TimeSpan) Refused(string hash, string check, object? value)
    {
        logger.LogWarning("Gravatar answer refused for {Hash}: {Check} {Value}", hash, check, value);
        return (null, MissLifetime);
    }
}
