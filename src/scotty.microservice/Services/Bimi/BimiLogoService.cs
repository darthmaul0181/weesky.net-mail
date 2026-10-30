using System.Net;
using Microsoft.Extensions.Logging;
using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Microservice.Services.Bimi;

internal sealed class BimiLogoService(
    IDnsTxtResolver dns, IHttpClientFactory clients, ISvgRasterizer rasterizer, TimeProvider clock,
    ILogger<BimiLogoService> logger) : IBimiLogoService, IDisposable
{
    public const string ClientName = "bimi";
    public const int MaxBytes = 32 * 1024;
    public const int RasterSize = 80;
    public const int MaxRedirects = 2;
    public static readonly TimeSpan FetchTimeout = GravatarService.FetchTimeout;
    private const long CacheBytes = 32 * 1024 * 1024;
    private const string SvgType = "image/svg+xml";

    private readonly AnswerCache cache = new(clock, CacheBytes);

    public Task<AvatarImage?> GetAsync(string domain, string selector, CancellationToken cancellationToken) =>
        cache.GetAsync($"{domain}|{selector}", () => FetchAsync(domain, selector), cancellationToken);

    public void Dispose() => cache.Dispose();

    private static IEnumerable<string> Candidates(string domain)
    {
        yield return domain;
        var parent = domain[(domain.IndexOf('.') + 1)..];
        if (parent.Contains('.')) yield return parent;
    }

    private async Task<(AvatarImage? Image, TimeSpan Lifetime)> FetchAsync(string domain, string selector)
    {
        // HttpClient.Timeout stops at the headers: one budget covers the lookup, the headers and the body.
        using var budget = new CancellationTokenSource(FetchTimeout, clock);
        try
        {
            var logo = await FindLogoAsync(domain, selector, budget.Token);
            return logo is null ? (null, GravatarService.MissLifetime) : await DownloadAsync(domain, logo, budget.Token);
        }
        // No visitor's token reaches here: a cancellation is the budget, and a resolver may throw anything.
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BIMI unavailable for {Domain}", domain);
            return (null, GravatarService.FailureLifetime);
        }
    }

    private async Task<Uri?> FindLogoAsync(string domain, string selector, CancellationToken cancellationToken)
    {
        foreach (var name in Candidates(domain))
        {
            var records = await dns.GetTxtAsync($"{selector}._bimi.{name}", cancellationToken);
            if (BimiRecord.Exists(records)) return BimiRecord.LogoOf(records);
        }

        return null;
    }

    private async Task<(AvatarImage?, TimeSpan)> DownloadAsync(string domain, Uri logo, CancellationToken cancellationToken)
    {
        using var response = await FollowAsync(clients.CreateClient(ClientName), logo, cancellationToken);
        var status = response.StatusCode;
        if (status == HttpStatusCode.TooManyRequests || (int)status >= 500) return Failed(domain, "status", status);
        if ((int)status is >= 300 and < 400)
            return Refused(domain, "redirect", (From: response.RequestMessage?.RequestUri, Location: response.Headers.Location?.OriginalString ?? "none"));
        if (status != HttpStatusCode.OK) return Refused(domain, "status", status);

        var type = response.Content.Headers.ContentType?.MediaType;
        if (!SvgType.Equals(type, StringComparison.OrdinalIgnoreCase)) return Refused(domain, "content type", type);
        var svg = await response.Content.ReadCappedAsync(MaxBytes, cancellationToken);
        if (svg is null or []) return Refused(domain, "body", svg is null ? "over the size cap" : "empty");
        if (!SvgGuard.IsSafe(svg)) return Refused(domain, "svg", "unsafe");

        var png = await rasterizer.ToPngAsync(svg, RasterSize, CancellationToken.None);
        return png is null ? Refused(domain, "raster", "failed") : (new AvatarImage(png, "image/png"), GravatarService.HitLifetime);
    }

    // The handler never redirects on its own: each hop is re-validated here, and dialled through PublicOnlyConnect again.
    private static async Task<HttpResponseMessage> FollowAsync(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        for (var redirects = 0; ; redirects++)
        {
            var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (redirects == MaxRedirects || RedirectTarget(uri, response) is not { } next) return response;
            response.Dispose();
            uri = next;
        }
    }

    private static Uri? RedirectTarget(Uri from, HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect
        && response.Headers.Location is { } location && Uri.TryCreate(from, location, out var to) && BimiRecord.IsFetchable(to)
            ? to
            : null;

    private (AvatarImage?, TimeSpan) Failed(string domain, string check, object? value)
    {
        logger.LogWarning("BIMI logo unavailable for {Domain}: {Check} {Value}", domain, check, value);
        return (null, GravatarService.FailureLifetime);
    }

    private (AvatarImage?, TimeSpan) Refused(string domain, string check, object? value)
    {
        logger.LogWarning("BIMI logo refused for {Domain}: {Check} {Value}", domain, check, value);
        return (null, GravatarService.MissLifetime);
    }
}
