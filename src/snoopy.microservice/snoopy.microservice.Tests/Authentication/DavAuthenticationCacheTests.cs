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

        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true));

        Assert.True(cache.TryGet("alice@weesky.be", "fingerprint-a", out var identity));
        Assert.Equal(User, identity.UserId);
        Assert.True(identity.CardDavEnabled);
    }

    [Fact]
    public void TryGet_MissesOnAnotherFingerprint()
    {
        // A replaced secret must not be served from the cache of the one it replaced.
        var (cache, _) = Create();
        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true));

        Assert.False(cache.TryGet("alice@weesky.be", "fingerprint-b", out _));
    }

    [Fact]
    public void TryGet_MissesOnAnotherIdentifier()
    {
        var (cache, _) = Create();
        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true));

        Assert.False(cache.TryGet("bob@weesky.be", "fingerprint-a", out _));
    }

    [Fact]
    public void TryGet_MissesOnceTheWindowHasPassed()
    {
        var (cache, clock) = Create();
        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true));

        clock.Now = clock.Now.Add(SessionGuardWindow + TimeSpan.FromSeconds(1));

        Assert.False(cache.TryGet("alice@weesky.be", "fingerprint-a", out _));
    }

    [Fact]
    public void Forget_DropsTheEntryImmediately()
    {
        // What a regeneration and a security-stamp rotation both call, so the replaced secret
        // stops working on this instance at once rather than at the end of the window.
        var (cache, _) = Create();
        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true));

        cache.Forget("alice@weesky.be");

        Assert.False(cache.TryGet("alice@weesky.be", "fingerprint-a", out _));
    }

    [Fact]
    public void CachedIdentityCarriesTheSwitch_SoADisabledAccountIsNotServedForAMinute()
    {
        var (cache, _) = Create();

        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, false));

        Assert.True(cache.TryGet("alice@weesky.be", "fingerprint-a", out var identity));
        Assert.False(identity.CardDavEnabled);
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

    private static TimeSpan SessionGuardWindow => TimeSpan.FromSeconds(60);
}
