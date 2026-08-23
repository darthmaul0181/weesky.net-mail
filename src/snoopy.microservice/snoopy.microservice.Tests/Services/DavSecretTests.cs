using System.Security.Cryptography;
using System.Text;
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
        var secret = DavSecret.Generate();

        Assert.Equal(DavSecret.Length, secret.Length);
        // The base32 alphabet carries no whitespace, which is what makes the Trim below safe.
        Assert.All(secret, c => Assert.Contains(c, "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"));
    }

    [Fact]
    public void Hash_IsTheLowerCaseHexOfSaltThenSecret()
    {
        byte[] salt = [.. Enumerable.Range(0, DavSecret.SaltLength).Select(i => (byte)i)];

        var hash = DavSecret.Hash(salt, "ABCDEFGHIJKLMNOPQRST");

        var expected = Convert.ToHexStringLower(
            SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes("ABCDEFGHIJKLMNOPQRST")]));
        Assert.Equal(expected, hash);
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
        // Never the stored digest: that one is salted, this one is only ever a cache key.
        Assert.NotEqual(DavSecret.Hash(DavSecret.NewSalt(), secret), DavSecret.Fingerprint(secret));
    }
}
