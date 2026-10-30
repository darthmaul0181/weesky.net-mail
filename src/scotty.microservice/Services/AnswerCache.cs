using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Microservice.Services;

internal sealed class AnswerCache(TimeProvider clock, long sizeBytes) : IDisposable
{
    private const long AnswerWithoutImageCost = 512;

    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = sizeBytes });
    private readonly ConcurrentDictionary<string, Lazy<Task<AvatarImage?>>> inFlight = new(StringComparer.Ordinal);

    public async Task<AvatarImage?> GetAsync(
        string key, Func<Task<(AvatarImage? Image, TimeSpan Lifetime)>> fetch, CancellationToken cancellationToken)
    {
        if (TryHeld(key, out var held)) return held;

        // Shared by every caller: one leaving stops waiting, it never cancels the lookup the others wait on.
        var lookup = inFlight.GetOrAdd(key, k => new Lazy<Task<AvatarImage?>>(() => LookUpAsync(k, fetch)));
        return await lookup.Value.WaitAsync(cancellationToken);
    }

    public void Dispose() => cache.Dispose();

    // The cache's own expiry runs on the wall clock and only bounds memory; freshness is judged on ours.
    private bool TryHeld(string key, out AvatarImage? image)
    {
        image = null;
        if (!cache.TryGetValue(key, out Answer? answer) || clock.GetUtcNow() >= answer!.ExpiresAt) return false;
        image = answer.Image;
        return true;
    }

    private async Task<AvatarImage?> LookUpAsync(string key, Func<Task<(AvatarImage? Image, TimeSpan Lifetime)>> fetch)
    {
        try
        {
            // A lookup that finished between the caller's check and this one has already answered.
            if (TryHeld(key, out var held)) return held;

            var (image, lifetime) = await fetch();
            cache.Set(key, new Answer(image, clock.GetUtcNow() + lifetime), new MemoryCacheEntryOptions
            {
                Size = image?.Bytes.Length ?? AnswerWithoutImageCost,
                AbsoluteExpirationRelativeToNow = lifetime
            });
            return image;
        }
        finally
        {
            inFlight.TryRemove(key, out _);
        }
    }

    private sealed record Answer(AvatarImage? Image, DateTimeOffset ExpiresAt);
}
