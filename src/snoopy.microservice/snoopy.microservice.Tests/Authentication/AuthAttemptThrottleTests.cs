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
    public void AWhitespaceAddress_NeverEntersAKey()
    {
        var (throttle, _) = Create();

        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure($"user{i}@weesky.be", "   ");

        Assert.False(throttle.IsBlocked("someone-else@weesky.be", "   ", out _));
    }

    [Fact]
    public void RecordSuccess_OnAnIdentifierNeverSeen_DoesNothing()
    {
        var (throttle, _) = Create();

        throttle.RecordSuccess("nobody@weesky.be");

        Assert.False(throttle.IsBlocked("nobody@weesky.be", null, out _));
    }

    [Fact]
    public void TwoAddressesInTheSameIPv6SlashSixtyFour_ShareACounter()
    {
        var (throttle, _) = Create();

        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure($"user{i}@weesky.be", "2001:db8:1234:5678::1");

        // Same /64, different host bits: an attacker sprays free addresses inside a prefix unless
        // the key aggregates it. A household sharing a /64 shares the counter, the intended trade.
        Assert.True(throttle.IsBlocked("someone-else@weesky.be", "2001:db8:1234:5678::dead:beef", out _));
    }

    [Fact]
    public void TwoIPv4Addresses_DoNotShareACounter()
    {
        var (throttle, _) = Create();

        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure($"user{i}@weesky.be", "203.0.113.7");

        Assert.False(throttle.IsBlocked("someone-else@weesky.be", "203.0.113.8", out _));
    }

    [Fact]
    public void TwoIPv4MappedAddresses_DoNotShareACounter()
    {
        // Kestrel reports an IPv4 peer on a dual-stack socket in this mapped form; unmasked, every
        // such address would collapse to the same /64 and block every IPv4 client at once.
        var (throttle, _) = Create();

        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure($"user{i}@weesky.be", "::ffff:203.0.113.7");

        Assert.False(throttle.IsBlocked("someone-else@weesky.be", "::ffff:198.51.100.9", out _));
    }

    [Fact]
    public void AnIPv4MappedAddress_SharesACounterWithItsPlainForm()
    {
        // The two spellings name the same host, so they must key to the same counter.
        var (throttle, _) = Create();

        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure($"user{i}@weesky.be", "::ffff:203.0.113.7");

        Assert.True(throttle.IsBlocked("someone-else@weesky.be", "203.0.113.7", out _));
    }

    [Fact]
    public void ThePartialWindow_DropsOnlyTheStampsThatAged()
    {
        var (throttle, clock) = Create();

        // First batch: five failures at t0.
        for (var i = 0; i < 5; i++) throttle.RecordFailure("alice@weesky.be", "203.0.113.7");
        clock.Now = clock.Now.Add(TimeSpan.FromMinutes(10));

        // Second batch: five more at t0+10min, reaching the threshold (5+5).
        for (var i = 0; i < 5; i++) throttle.RecordFailure("alice@weesky.be", "203.0.113.7");

        clock.Now = clock.Now.Add(TimeSpan.FromMinutes(6));

        // At t0+16min the first batch (16min old) has aged out of the 15min window; the second
        // batch (6min old) survives but alone is under the threshold.
        Assert.False(throttle.IsBlocked("alice@weesky.be", "203.0.113.7", out _));

        // Third batch: five more, refilling to the threshold on top of the surviving second batch.
        for (var i = 0; i < 5; i++) throttle.RecordFailure("alice@weesky.be", "203.0.113.7");

        Assert.True(throttle.IsBlocked("alice@weesky.be", "203.0.113.7", out var retryAfter));
        // Derived from the second batch's oldest surviving stamp (6min old), not restarted from
        // the fresh third batch, which would otherwise report the full 15min window.
        Assert.Equal(TimeSpan.FromMinutes(9), retryAfter);
    }

    [Fact]
    public void TheCapOnOneKey_LetsTheWindowExpireDespiteContinuedAttack()
    {
        var (throttle, clock) = Create();

        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure("alice@weesky.be", "203.0.113.7");

        clock.Now = clock.Now.Add(TimeSpan.FromMinutes(1));

        // Past the cap these are refused, not appended. Uncapped, each would carry its own
        // fifteen-minute expiry from t0+1min and keep the key blocked until t0+16min — past the
        // point this test checks.
        for (var i = 0; i < 100; i++)
            throttle.RecordFailure("alice@weesky.be", "203.0.113.7");

        clock.Now = clock.Now.Add(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(1));

        Assert.False(throttle.IsBlocked("alice@weesky.be", "203.0.113.7", out _));
    }

    [Fact]
    public void TheMemoryIsBounded_SoAnAttackerCannotGrowIt()
    {
        // The keys are values the attacker chooses. Without a ceiling the counter is itself the
        // memory exhaustion an unauthenticated request must not be able to cause. A distinct
        // address per iteration exercises the two-key-per-call path task 7 will actually take.
        var (throttle, _) = Create();

        for (var i = 0; i < AuthAttemptThrottle.MaxTrackedKeys * 2; i++)
            throttle.RecordFailure($"user{i}@weesky.be", $"203.0.113.{i}");

        // One more call lets a pending batch eviction settle the table back under the ceiling.
        throttle.RecordFailure("settle@weesky.be", "198.51.100.99");

        Assert.InRange(throttle.TrackedKeys, 0, AuthAttemptThrottle.MaxTrackedKeys);
    }
}
