using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class DavSecretTests
{
    [Fact]
    public void Generate_DrawsADistinctSecretEveryTime()
    {
        var drawn = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 200; i++) drawn.Add(DavSecret.Generate());

        Assert.Equal(200, drawn.Count);
    }

    [Fact]
    public void Generate_IsTwentyBase32Characters()
    {
        // Looped: an alphabet is a property of every draw, and one sample would let a stray
        // character through on 199 out of 200 occasions.
        for (var i = 0; i < 200; i++)
        {
            var secret = DavSecret.Generate();

            Assert.Equal(DavSecret.Length, secret.Length);
            // The base32 alphabet carries no whitespace, which is what makes the Trim below safe.
            Assert.All(secret, c => Assert.Contains(c, "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"));
        }
    }

    [Fact]
    public void Hash_IsTheLowerCaseHexOfSaltThenSecret()
    {
        // A golden vector, computed outside this codebase. Recomputing the expected value with the
        // implementation's own expression would let the concatenation order or the hex case change
        // while the test stayed green — and every digest already stored would become unverifiable,
        // with no way back to the secrets.
        byte[] salt = [.. Enumerable.Range(0, DavSecret.SaltLength).Select(i => (byte)i)];

        var hash = DavSecret.Hash(salt, "ABCDEFGHIJKLMNOPQRST");

        Assert.Equal("b4900c87f3c76ab6732dc9b0c79cffdf44b13cbca89975ffb9969facb8003f24", hash);
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void Hash_DiffersForTheSameSecretUnderTwoSalts()
    {
        // What the per-row salt buys: the same string drawn twice does not recognise itself in
        // the table.
        var first = DavSecret.Hash(DavSecret.NewSalt(), "ABCDEFGHIJKLMNOPQRST");
        var second = DavSecret.Hash(DavSecret.NewSalt(), "ABCDEFGHIJKLMNOPQRST");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void NewSalt_IsSixteenBytesAndNeverTheSameTwice()
    {
        var first = DavSecret.NewSalt();
        var second = DavSecret.NewSalt();

        Assert.Equal(DavSecret.SaltLength, first.Length);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Matches_AcceptsTheSecretItHashed()
    {
        var salt = DavSecret.NewSalt();
        var secret = DavSecret.Generate();

        Assert.True(DavSecret.Matches(salt, DavSecret.Hash(salt, secret), secret));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void Matches_IgnoresEdgeWhitespaceOnThePresentedSecret(string blank)
    {
        // Copy-paste — mobile above all — adds these, and the base32 alphabet holds none of them.
        // Without the Trim the symptom is a correct password refused, indistinguishable from a typo.
        var salt = DavSecret.NewSalt();
        var secret = DavSecret.Generate();

        Assert.True(DavSecret.Matches(salt, DavSecret.Hash(salt, secret), $"{blank}{secret}{blank}"));
    }

    [Fact]
    public void Matches_RefusesAnotherSecret()
    {
        var salt = DavSecret.NewSalt();

        Assert.False(DavSecret.Matches(salt, DavSecret.Hash(salt, DavSecret.Generate()), DavSecret.Generate()));
    }

    [Fact]
    public void Matches_RefusesTheRightSecretUnderTheWrongSalt()
    {
        // The case that makes the salt load-bearing rather than decorative: a Hash ignoring its
        // salt parameter passes every other test here, and this one alone catches it.
        var secret = DavSecret.Generate();
        var stored = DavSecret.Hash(DavSecret.NewSalt(), secret);

        Assert.False(DavSecret.Matches(DavSecret.NewSalt(), stored, secret));
    }

    [Fact]
    public void Matches_RefusesAnEmptyOrMalformedStoredHash()
    {
        var salt = DavSecret.NewSalt();

        Assert.False(DavSecret.Matches(salt, string.Empty, DavSecret.Generate()));
        Assert.False(DavSecret.Matches(salt, "not-hex", DavSecret.Generate()));
    }

    [Fact]
    public void Fingerprint_IsSaltFreeAndStableForTheSameSecret()
    {
        var secret = DavSecret.Generate();

        Assert.Equal(DavSecret.Fingerprint(secret), DavSecret.Fingerprint(secret));
        Assert.NotEqual(DavSecret.Fingerprint(secret), DavSecret.Fingerprint(DavSecret.Generate()));
        // It must trim exactly as Matches does, or the burst cache misses on every request from a
        // client that appends a newline — the same secret, keyed twice.
        Assert.Equal(DavSecret.Fingerprint(secret), DavSecret.Fingerprint($" {secret}\r\n"));
        // Never the stored digest: that one is salted, this one is only ever a cache key.
        Assert.NotEqual(DavSecret.Hash(DavSecret.NewSalt(), secret), DavSecret.Fingerprint(secret));
    }
}
