using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Authentication;

public sealed class AuthAttemptThrottleTests
{
    private static (AuthAttemptThrottle Throttle, MutableTimeProvider Clock) Create()
    {
        var clock = new MutableTimeProvider();
        return (new AuthAttemptThrottle(clock), clock);
    }

    [Fact]
    public void UnderTheThreshold_NothingIsBlocked()
    {
        var (throttle, _) = Create();

        for (var i = 0; i < AuthAttemptThrottle.MaxFailures - 1; i++)
            throttle.RecordFailure("alice@weesky.be", "203.0.113.7");

        Assert.False(throttle.IsBlocked("alice@weesky.be", "203.0.113.7", out _));
    }

    [Fact]
    public void AtTheThreshold_TheIdentifierIsBlockedFromEverywhere()
    {
        // One account attacked from many machines: the identifier is what carries the count.
        var (throttle, _) = Create();

        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure("alice@weesky.be", $"203.0.113.{i}");

        Assert.True(throttle.IsBlocked("alice@weesky.be", "198.51.100.1", out var retryAfter));
        Assert.InRange(retryAfter, TimeSpan.Zero, AuthAttemptThrottle.Window);
    }

    [Fact]
    public void AtTheThreshold_TheAddressIsBlockedForEveryIdentifier()
    {
        // Many accounts attacked from one machine: the address is what carries the count.
        var (throttle, _) = Create();

        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure($"user{i}@weesky.be", "203.0.113.7");

        Assert.True(throttle.IsBlocked("someone-else@weesky.be", "203.0.113.7", out _));
    }

    [Fact]
    public void TheWindowSlides_SoTheBlockLiftsOnItsOwn()
    {
        var (throttle, clock) = Create();
        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure("alice@weesky.be", "203.0.113.7");

        clock.Now = clock.Now.Add(AuthAttemptThrottle.Window + TimeSpan.FromSeconds(1));

        Assert.False(throttle.IsBlocked("alice@weesky.be", "203.0.113.7", out _));
    }

    [Fact]
    public void RetryAfter_IsWhatIsLeftOfTheWindowOnTheOldestFailure()
    {
        var (throttle, clock) = Create();
        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure("alice@weesky.be", "203.0.113.7");

        clock.Now = clock.Now.Add(TimeSpan.FromMinutes(5));

        Assert.True(throttle.IsBlocked("alice@weesky.be", "203.0.113.7", out var retryAfter));
        Assert.Equal(TimeSpan.FromMinutes(10), retryAfter);
    }

    [Fact]
    public void ASuccessClearsTheIdentifier_SoTheRealPhoneGetsBackIn()
    {
        var (throttle, _) = Create();
        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure("alice@weesky.be", "203.0.113.7");

        throttle.RecordSuccess("alice@weesky.be");

        Assert.False(throttle.IsBlocked("alice@weesky.be", "198.51.100.1", out _));
    }

    [Fact]
    public void ASuccessDoesNotAbsolveTheAddressItCameFrom()
    {
        var (throttle, _) = Create();
        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure($"user{i}@weesky.be", "203.0.113.7");

        throttle.RecordSuccess("user0@weesky.be");

        Assert.True(throttle.IsBlocked("user0@weesky.be", "203.0.113.7", out _));
    }

    [Fact]
    public void AnUnknownAddress_NeverEntersAKey()
    {
        var (throttle, _) = Create();

        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure($"user{i}@weesky.be", null);

        Assert.False(throttle.IsBlocked("someone-else@weesky.be", null, out _));
    }

    [Fact]
    public void TheMemoryIsBounded_SoAnAttackerCannotGrowIt()
    {
        // The keys are values the attacker chooses. Without a ceiling the counter is itself the
        // memory exhaustion an unauthenticated request must not be able to cause.
        var (throttle, _) = Create();

        for (var i = 0; i < AuthAttemptThrottle.MaxTrackedKeys * 2; i++)
            throttle.RecordFailure($"user{i}@weesky.be", null);

        Assert.InRange(throttle.TrackedKeys, 0, AuthAttemptThrottle.MaxTrackedKeys);
    }
}
