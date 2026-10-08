using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Microservice.Services;

internal sealed class DailyImageService(
    IHttpClientFactory clients, TimeProvider clock, ILogger<DailyImageService> logger) : IDailyImageService
{
    public const string ClientName = "bing";
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(5);

    // Bounded: BingArchive.MarketOf only ever answers two markets.
    private readonly ConcurrentDictionary<string, Slot> slots = new(StringComparer.Ordinal);

    public async Task<DailyImage?> GetAsync(string market, CancellationToken cancellationToken)
    {
        var slot = slots.GetOrAdd(market, _ => new Slot());
        if (TryServe(slot.State, out var served)) return served;

        await slot.Gate.WaitAsync(cancellationToken);
        try
        {
            if (TryServe(slot.State, out served)) return served;

            var fresh = await FetchAsync(market, cancellationToken);
            slot.State = fresh is null
                ? slot.State with { RetryAfter = clock.GetUtcNow() + RetryDelay }
                : new SlotState(fresh, DateTimeOffset.MinValue);
            return slot.State.Current;
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    // Fresh, or failed too recently to ask again: either way the answer is what the slot holds.
    private bool TryServe(SlotState state, out DailyImage? image)
    {
        var now = clock.GetUtcNow();
        image = state.Current;
        return (image is not null && now < image.ExpiresAt) || now < state.RetryAfter;
    }

    private async Task<DailyImage?> FetchAsync(string market, CancellationToken cancellationToken)
    {
        // HttpClient.Timeout stops at the headers: a body dripped by Bing would hold the gate for everyone.
        using var budget = new CancellationTokenSource(FetchTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        var token = linked.Token;
        try
        {
            var client = clients.CreateClient(ClientName);

            using var archive = await client.GetAsync(
                BingArchive.MetadataUrl(market), HttpCompletionOption.ResponseHeadersRead, token);
            if (archive.StatusCode != HttpStatusCode.OK) return Refused(market, "archive not answered with 200");
            var json = await ReadCappedAsync(archive.Content, BingArchive.MaxMetadataBytes, token);
            if (json is null) return Refused(market, "archive too large");

            var entry = BingArchive.Parse(Encoding.UTF8.GetString(json), clock.GetUtcNow());
            if (entry.IsFailure) return Refused(market, entry.Error);

            using var image = await client.GetAsync(
                entry.Value.ImageUrl, HttpCompletionOption.ResponseHeadersRead, token);
            if (image.StatusCode != HttpStatusCode.OK || image.Content.Headers.ContentType?.MediaType != "image/jpeg")
                return Refused(market, "image not a JPEG answered with 200");
            var bytes = await ReadCappedAsync(image.Content, BingArchive.MaxImageBytes, token);
            if (bytes is null) return Refused(market, "image too large");

            var e = entry.Value;
            return new DailyImage(e.Version, e.Title, e.Copyright, bytes, e.ExpiresAt);
        }
        // IOException: a body cut short (HttpIOException). A cancellation not the visitor's is the budget or the timeout.
        catch (Exception ex) when (ex is HttpRequestException or IOException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Image of the day unavailable for {Market}", market);
            return null;
        }
    }

    private DailyImage? Refused(string market, string reason)
    {
        logger.LogWarning("Image of the day refused for {Market}: {Reason}", market, reason);
        return null;
    }

    // The declared length is checked first, the stream still counted: a server may omit or lie about it.
    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, int max, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > max) return null;

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > max) return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private sealed record SlotState(DailyImage? Current, DateTimeOffset RetryAfter);

    private sealed class Slot
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public volatile SlotState State = new(null, DateTimeOffset.MinValue);
    }
}
