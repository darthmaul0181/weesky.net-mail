using System;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// One KEK is derived once and shared across tests: 600k PBKDF2 iterations cost ~100 ms, and
/// deriving it per-test would make the suite noticeably slower for no added coverage.
/// </summary>
public sealed class ConnectedAccountCipherTests
{
    private static readonly byte[] Salt = ConnectedAccountCipher.NewSalt();
    private static readonly byte[] Kek = ConnectedAccountCipher.DeriveKek("correct-horse-battery-staple", Salt);

    // '密' is 3 UTF-8 bytes: padding with 1-byte 'a's to an exact byte total proves the guard
    // counts bytes, not chars, on both sides of the boundary.
    private static string SecretOfByteLength(int length) => "密" + new string('a', length - 3);

    [Fact]
    public void EncryptThenDecrypt_RoundTrips()
    {
        const string secret = "sûreté-müe-😀-密码";

        var cipher = ConnectedAccountCipher.Encrypt(Kek, secret);
        var result = ConnectedAccountCipher.Decrypt(Kek, cipher);

        Assert.True(result.IsSuccess);
        Assert.Equal(secret, result.Value);
    }

    [Fact]
    public void Encrypt_ProducesADifferentCipherEachTime()
    {
        var first = ConnectedAccountCipher.Encrypt(Kek, "hunter2");
        var second = ConnectedAccountCipher.Encrypt(Kek, "hunter2");

        Assert.NotEqual<byte[]>(first, second);
    }

    [Fact]
    public void Decrypt_FailsUnderAnotherKek()
    {
        var otherKek = ConnectedAccountCipher.DeriveKek("a-different-password", ConnectedAccountCipher.NewSalt());
        var cipher = ConnectedAccountCipher.Encrypt(Kek, "hunter2");

        var result = ConnectedAccountCipher.Decrypt(otherKek, cipher);

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.CredentialsInvalid, result.Error);
    }

    [Fact]
    public void Decrypt_FailsOnATamperedByte()
    {
        var cipher = ConnectedAccountCipher.Encrypt(Kek, "hunter2");
        // Index by layout, not by end-of-buffer: cipher[^1] only lands in the ciphertext by
        // accident of this secret's length and would silently flip a tag byte with a shorter one.
        cipher[ConnectedAccountCipher.NonceLength + ConnectedAccountCipher.TagLength] ^= 0xFF;

        var result = ConnectedAccountCipher.Decrypt(Kek, cipher);

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.CredentialsInvalid, result.Error);
    }

    [Fact]
    public void Decrypt_FailsOnATruncatedBuffer()
    {
        var result = ConnectedAccountCipher.Decrypt(Kek, new byte[27]);

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.CredentialsInvalid, result.Error);
    }

    [Fact]
    public void Encrypt_RoundTripsASecretAtExactlyTheMaxLength()
    {
        var secret = SecretOfByteLength(ConnectedAccountCipher.MaxSecretLength);

        var cipher = ConnectedAccountCipher.Encrypt(Kek, secret);
        var result = ConnectedAccountCipher.Decrypt(Kek, cipher);

        Assert.True(result.IsSuccess);
        Assert.Equal(secret, result.Value);
    }

    [Fact]
    public void Encrypt_RefusesASecretOneByteOverTheMaxLength()
    {
        var secret = SecretOfByteLength(ConnectedAccountCipher.MaxSecretLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => ConnectedAccountCipher.Encrypt(Kek, secret));
    }

    [Fact]
    public void DeriveKek_IsDeterministicPerSalt()
    {
        var again = ConnectedAccountCipher.DeriveKek("correct-horse-battery-staple", Salt);
        var otherSaltKek = ConnectedAccountCipher.DeriveKek(
            "correct-horse-battery-staple", ConnectedAccountCipher.NewSalt());

        Assert.Equal<byte[]>(Kek, again);
        Assert.NotEqual<byte[]>(Kek, otherSaltKek);
    }
}
