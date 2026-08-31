using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Authentication;

/// <summary>
/// The sixty-second residue on revocation: <c>Forget</c> cannot beat a concurrent <c>Store</c>,
/// because a request that read the old secret BEFORE the rotation can write it back AFTER.
/// </summary>
public sealed class DavAuthenticationCacheGenerationTests
{
    private const string Fingerprint = "fingerprint-a";
    private static readonly DavIdentity Identity =
        new(Guid.Parse("66666666-6666-6666-6666-666666666666"), true);

    private static DavAuthenticationCache Create() => new(new MutableTimeProvider());

    [Fact]
    public void AStoreThatReadBeforeARevocation_IsRefusedAfterIt()
    {
        var cache = Create();
        var generation = cache.Generation("alice@weesky.be");   // the reader takes it BEFORE

        cache.Forget("alice@weesky.be");                        // the rotation happens
        // Never assumed: two equal generations would make the refusal below unfalsifiable.
        Assert.NotEqual(generation, cache.Generation("alice@weesky.be"));

        cache.Store("alice@weesky.be", Fingerprint, Identity, generation);

        // Forget cannot beat a concurrent Store: a request that read the old secret before the
        // rotation could write it back after. The generation counter is what closes the sixty-second
        // window — and sixty seconds of a revoked secret still working is the whole point.
        Assert.False(cache.TryGet("alice@weesky.be", Fingerprint, out _));
    }

    [Fact]
    public void ARevokedSecretIsNotResurrectedByAnEntryThatSurvivedTheRotation()
    {
        // The same race read from the other end: the entry the rotation removed must stay removed,
        // whatever the in-flight request writes afterwards.
        var cache = Create();
        var generation = cache.Generation("alice@weesky.be");
        cache.Store("alice@weesky.be", Fingerprint, Identity, generation);
        Assert.True(cache.TryGet("alice@weesky.be", Fingerprint, out _));

        cache.Forget("alice@weesky.be");
        cache.Store("alice@weesky.be", Fingerprint, Identity, generation);

        Assert.False(cache.TryGet("alice@weesky.be", Fingerprint, out _));
    }

    [Fact]
    public void AStoreTakenAfterTheRevocation_IsAccepted()
    {
        var cache = Create();
        cache.Forget("alice@weesky.be");
        var generation = cache.Generation("alice@weesky.be");   // taken AFTER

        cache.Store("alice@weesky.be", Fingerprint, Identity, generation);

        Assert.True(cache.TryGet("alice@weesky.be", Fingerprint, out _));
    }

    [Fact]
    public void TheGenerationIsPerIdentifier()
    {
        var cache = Create();
        var generation = cache.Generation("alice@weesky.be");

        cache.Forget("bob@weesky.be");
        cache.Store("alice@weesky.be", Fingerprint, Identity, generation);

        // Revoking one user must not evict every other user's cache entry — that would turn one
        // password change into a thundering herd of database reads.
        Assert.True(cache.TryGet("alice@weesky.be", Fingerprint, out _));
    }

    [Fact]
    public void TheGenerationSurvivesAnEntryExpiring()
    {
        // The counter must not live inside the cache ENTRY: an entry that expires would take the
        // generation with it, and the next Store would be accepted under a stale one.
        var cache = Create();
        cache.Forget("alice@weesky.be");
        var afterRevocation = cache.Generation("alice@weesky.be");

        cache.Store("alice@weesky.be", Fingerprint, Identity, afterRevocation);
        cache.Forget("alice@weesky.be");

        Assert.NotEqual(afterRevocation, cache.Generation("alice@weesky.be"));
    }

    [Fact]
    public void TheGenerationOfAnUnknownIdentifier_IsNotRemembered()
    {
        // Generation runs before the database read, on whatever identifier the Basic header names,
        // so a reader that created a row would hand an attacker an unbounded table to grow.
        var cache = Create();

        for (var i = 0; i < 1_000; i++) cache.Generation($"attacker-{i}@weesky.be");

        Assert.Equal(0, cache.TrackedGenerations);
    }
}
