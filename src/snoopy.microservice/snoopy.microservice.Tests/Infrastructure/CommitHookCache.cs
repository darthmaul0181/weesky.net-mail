using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// A real <see cref="MemoryCache"/> whose entries run a hook at the moment they are committed —
/// <see cref="ICacheEntry.Dispose"/>, which is where <c>Set</c> and <c>GetOrCreate</c> publish the
/// value. It is the only way to place a concurrent write inside the window between a cached read
/// computing its answer and that answer becoming visible.
/// </summary>
internal sealed class CommitHookCache(Func<object, bool> hookedKeys) : IMemoryCache
{
    private readonly MemoryCache _inner = new(new MemoryCacheOptions());
    private Action? _onNextCommit;

    /// <summary>Runs once, before the next hooked entry is committed.</summary>
    public void BeforeNextCommit(Action action) => _onNextCommit = action;

    public ICacheEntry CreateEntry(object key)
    {
        var entry = _inner.CreateEntry(key);
        return hookedKeys(key) ? new HookedEntry(entry, TakeHook) : entry;
    }

    public bool TryGetValue(object key, out object? value) => _inner.TryGetValue(key, out value);

    public void Remove(object key) => _inner.Remove(key);

    public void Dispose() => _inner.Dispose();

    private void TakeHook()
    {
        var hook = _onNextCommit;
        _onNextCommit = null;
        hook?.Invoke();
    }

    private sealed class HookedEntry(ICacheEntry inner, Action onCommit) : ICacheEntry
    {
        public object Key => inner.Key;
        public object? Value { get => inner.Value; set => inner.Value = value; }
        public DateTimeOffset? AbsoluteExpiration { get => inner.AbsoluteExpiration; set => inner.AbsoluteExpiration = value; }
        public TimeSpan? AbsoluteExpirationRelativeToNow { get => inner.AbsoluteExpirationRelativeToNow; set => inner.AbsoluteExpirationRelativeToNow = value; }
        public TimeSpan? SlidingExpiration { get => inner.SlidingExpiration; set => inner.SlidingExpiration = value; }
        public IList<IChangeToken> ExpirationTokens => inner.ExpirationTokens;
        public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => inner.PostEvictionCallbacks;
        public CacheItemPriority Priority { get => inner.Priority; set => inner.Priority = value; }
        public long? Size { get => inner.Size; set => inner.Size = value; }

        public void Dispose()
        {
            onCommit();
            inner.Dispose();
        }
    }
}
