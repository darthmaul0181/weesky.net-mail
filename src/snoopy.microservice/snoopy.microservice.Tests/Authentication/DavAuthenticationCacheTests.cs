using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Authentication;

public sealed class DavAuthenticationCacheTests
{
    private static readonly Guid User = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static (DavAuthenticationCache Cache, MutableTimeProvider Clock) Create()
    {
        var clock = new MutableTimeProvider();
        return (new DavAuthenticationCache(clock), clock);
    }

    [Fact]
    public void Store_ThenTryGet_AnswersTheResolvedIdentity()
    {
        var (cache, _) = Create();

        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true),
            cache.Generation("alice@weesky.be"));

        Assert.True(cache.TryGet("alice@weesky.be", "fingerprint-a", out var identity));
        Assert.Equal(User, identity.UserId);
        Assert.True(identity.CardDavEnabled);
    }

    [Fact]
    public void TryGet_MissesOnAnotherFingerprint()
    {
        // A replaced secret must not be served from the cache of the one it replaced.
        var (cache, _) = Create();
        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true),
            cache.Generation("alice@weesky.be"));

        Assert.False(cache.TryGet("alice@weesky.be", "fingerprint-b", out _));
    }

    [Fact]
    public void TryGet_MissesOnAnotherIdentifier()
    {
        var (cache, _) = Create();
        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true),
            cache.Generation("alice@weesky.be"));

        Assert.False(cache.TryGet("bob@weesky.be", "fingerprint-a", out _));
    }

    [Fact]
    public void TryGet_HitsJustInsideTheWindow()
    {
        var (cache, clock) = Create();
        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true),
            cache.Generation("alice@weesky.be"));

        clock.Now = clock.Now.Add(DavAuthenticationCache.Window - TimeSpan.FromSeconds(1));

        Assert.True(cache.TryGet("alice@weesky.be", "fingerprint-a", out _));
    }

    [Fact]
    public void TryGet_MissesOnceTheWindowHasPassed()
    {
        var (cache, clock) = Create();
        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true),
            cache.Generation("alice@weesky.be"));

        clock.Now = clock.Now.Add(DavAuthenticationCache.Window + TimeSpan.FromSeconds(1));

        Assert.False(cache.TryGet("alice@weesky.be", "fingerprint-a", out _));
    }

    [Fact]
    public void Store_UnderTheSameIdentifier_ReplacesTheOnlyEntry()
    {
        // One entry per identifier, not per (identifier, fingerprint) pair: a second Store for
        // the same account retires the first fingerprint rather than keeping both alive.
        var (cache, _) = Create();
        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true),
            cache.Generation("alice@weesky.be"));

        cache.Store("alice@weesky.be", "fingerprint-b", new DavIdentity(User, true),
            cache.Generation("alice@weesky.be"));

        Assert.False(cache.TryGet("alice@weesky.be", "fingerprint-a", out _));
        Assert.True(cache.TryGet("alice@weesky.be", "fingerprint-b", out _));
    }

    [Fact]
    public void Forget_DropsTheEntryImmediately()
    {
        // What a regeneration and a security-stamp rotation both call, so the replaced secret
        // stops working on this instance at once rather than at the end of the window.
        var (cache, _) = Create();
        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true),
            cache.Generation("alice@weesky.be"));

        cache.Forget("alice@weesky.be");

        Assert.False(cache.TryGet("alice@weesky.be", "fingerprint-a", out _));
    }

    [Fact]
    public void Forget_LeavesAnotherIdentifiersEntryIntact()
    {
        var (cache, _) = Create();
        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true),
            cache.Generation("alice@weesky.be"));
        cache.Store("bob@weesky.be", "fingerprint-b", new DavIdentity(Guid.NewGuid(), true),
            cache.Generation("bob@weesky.be"));

        cache.Forget("alice@weesky.be");

        Assert.True(cache.TryGet("bob@weesky.be", "fingerprint-b", out _));
    }

    [Fact]
    public void TryGet_DoesNotCanonicaliseTheIdentifier_TheCallerMustHave()
    {
        // The contract (IDavAuthenticationCache) puts canonicalisation on the caller; this pins
        // that the cache itself compares byte for byte rather than compensating for casing.
        var (cache, _) = Create();
        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true),
            cache.Generation("alice@weesky.be"));

        Assert.False(cache.TryGet("Alice@weesky.be", "fingerprint-a", out _));
    }

    [Fact]
    public void CachedIdentityCarriesTheSwitch_SoADisabledAccountIsNotServedForAMinute()
    {
        var (cache, _) = Create();

        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, false),
            cache.Generation("alice@weesky.be"));

        Assert.True(cache.TryGet("alice@weesky.be", "fingerprint-a", out var identity));
        Assert.False(identity.CardDavEnabled);
    }

    [Fact]
    public void ACachedSwitchStateOutlivesTheSwitch_UntilTheCallerForgetsIt()
    {
        // Stored while the account was enabled, and the cache never re-reads: for the rest of the
        // window it keeps answering enabled whatever the switch did meanwhile. That staleness is
        // why the controller driving the switch forgets on enable as much as on disable.
        var (cache, _) = Create();
        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true),
            cache.Generation("alice@weesky.be"));

        Assert.True(cache.TryGet("alice@weesky.be", "fingerprint-a", out var stale));
        Assert.True(stale.CardDavEnabled);

        cache.Forget("alice@weesky.be");

        Assert.False(cache.TryGet("alice@weesky.be", "fingerprint-a", out _));
    }

    [Fact]
    public void ShouldTouch_IsTrueOnceThenFalseUntilTheHourHasPassed()
    {
        // Without this every PROPFIND is one write to a column the screen renders as "2 hours ago".
        var (cache, clock) = Create();

        Assert.True(cache.ShouldTouch(User));
        Assert.False(cache.ShouldTouch(User));

        clock.Now = clock.Now.Add(TimeSpan.FromMinutes(59));
        Assert.False(cache.ShouldTouch(User));

        clock.Now = clock.Now.Add(TimeSpan.FromMinutes(2));
        Assert.True(cache.ShouldTouch(User));
    }

    [Fact]
    public void ShouldTouch_IsPerUser()
    {
        var (cache, _) = Create();
        var other = Guid.NewGuid();

        Assert.True(cache.ShouldTouch(User));
        Assert.True(cache.ShouldTouch(other));
    }
}
