using System;
using System.Security.Cryptography;
using System.Text;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;
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

    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DomainId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string Email = "alice@provider.example";

    private static byte[] Context() =>
        ConnectedAccountCipher.Context(AccountId, UserId, DomainId, Email);

    // '密' is 3 UTF-8 bytes: padding with 1-byte 'a's to an exact byte total proves the guard
    // counts bytes, not chars, on both sides of the boundary.
    private static string SecretOfByteLength(int length) => "密" + new string('a', length - 3);

    /// <summary>
    /// A pre-binding blob, written the way this cipher wrote them before the associated data
    /// existed: nonce, tag, ciphertext, no version byte and no AAD. Rows in this shape are still
    /// in the database, so the migration path has to be exercised against the real thing rather
    /// than against a flag.
    /// </summary>
    private static byte[] LegacyEncrypt(byte[] kek, string secret, byte[]? nonce = null)
    {
        var plaintext = Encoding.UTF8.GetBytes(secret);
        nonce ??= RandomNumberGenerator.GetBytes(ConnectedAccountCipher.NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[ConnectedAccountCipher.TagLength];

        using var aes = new AesGcm(kek, ConnectedAccountCipher.TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var blob = new byte[nonce.Length + tag.Length + ciphertext.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, nonce.Length);
        ciphertext.CopyTo(blob, nonce.Length + tag.Length);
        return blob;
    }

    [Fact]
    public void EncryptThenDecrypt_RoundTrips()
    {
        const string secret = "sûreté-müe-😀-密码";

        var cipher = ConnectedAccountCipher.Encrypt(Kek, secret, Context());
        var result = ConnectedAccountCipher.Decrypt(Kek, cipher, Context());

        Assert.True(result.IsSuccess);
        Assert.Equal(secret, result.Value);
    }

    [Fact]
    public void Encrypt_ProducesADifferentCipherEachTime()
    {
        var first = ConnectedAccountCipher.Encrypt(Kek, "hunter2", Context());
        var second = ConnectedAccountCipher.Encrypt(Kek, "hunter2", Context());

        Assert.NotEqual<byte[]>(first, second);
    }

    [Fact]
    public void Decrypt_FailsUnderAnotherKek()
    {
        var otherKek = ConnectedAccountCipher.DeriveKek("a-different-password", ConnectedAccountCipher.NewSalt());
        var cipher = ConnectedAccountCipher.Encrypt(Kek, "hunter2", Context());

        var result = ConnectedAccountCipher.Decrypt(otherKek, cipher, Context());

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.CredentialsInvalid, result.Error);
    }

    [Fact]
    public void Decrypt_FailsOnATamperedByte()
    {
        var cipher = ConnectedAccountCipher.Encrypt(Kek, "hunter2", Context());
        // Index by layout, not by end-of-buffer: cipher[^1] only lands in the ciphertext by
        // accident of this secret's length and would silently flip a tag byte with a shorter one.
        cipher[1 + ConnectedAccountCipher.NonceLength + ConnectedAccountCipher.TagLength] ^= 0xFF;

        var result = ConnectedAccountCipher.Decrypt(Kek, cipher, Context());

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.CredentialsInvalid, result.Error);
    }

    [Fact]
    public void Decrypt_FailsOnATruncatedBuffer()
    {
        var result = ConnectedAccountCipher.Decrypt(Kek, new byte[27], Context());

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.CredentialsInvalid, result.Error);
    }

    [Fact]
    public void Encrypt_RoundTripsASecretAtExactlyTheMaxLength()
    {
        var secret = SecretOfByteLength(ConnectedAccountCipher.MaxSecretLength);

        var cipher = ConnectedAccountCipher.Encrypt(Kek, secret, Context());
        var result = ConnectedAccountCipher.Decrypt(Kek, cipher, Context());

        Assert.True(result.IsSuccess);
        Assert.Equal(secret, result.Value);
    }

    /// <summary>The whole blob has to fit connected_accounts.cipher, VARBINARY(8192).</summary>
    [Fact]
    public void Encrypt_FillsTheColumnExactlyAtTheMaxLength()
    {
        var cipher = ConnectedAccountCipher.Encrypt(
            Kek, SecretOfByteLength(ConnectedAccountCipher.MaxSecretLength), Context());

        Assert.Equal(8192, cipher.Length);
    }

    [Fact]
    public void Encrypt_RefusesASecretOneByteOverTheMaxLength()
    {
        var secret = SecretOfByteLength(ConnectedAccountCipher.MaxSecretLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ConnectedAccountCipher.Encrypt(Kek, secret, Context()));
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

    // ── The binding ───────────────────────────────────────
    //
    // The key is per-user, not per-account, so every one of these tampered reads decrypts fine
    // under the raw key. The tag is the only thing standing between a rewritten row and the
    // server handing a password to whatever host that row now names.

    /// <summary>The copy attack: one account's cipher moved onto another's row, same user.</summary>
    [Fact]
    public void Decrypt_FailsWhenTheCipherIsMovedToAnotherAccount()
    {
        var cipher = ConnectedAccountCipher.Encrypt(Kek, "hunter2", Context());

        var result = ConnectedAccountCipher.Decrypt(
            Kek, cipher, ConnectedAccountCipher.Context(Guid.NewGuid(), UserId, DomainId, Email));

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.CredentialsInvalid, result.Error);
    }

    /// <summary>
    /// The redirect attack, and the one a binding on the account id alone would have missed: the
    /// row keeps its identity and is repointed at a domain row naming the attacker's host.
    /// </summary>
    [Fact]
    public void Decrypt_FailsWhenTheRowIsRepointedAtAnotherDomain()
    {
        var cipher = ConnectedAccountCipher.Encrypt(Kek, "hunter2", Context());

        var result = ConnectedAccountCipher.Decrypt(
            Kek, cipher, ConnectedAccountCipher.Context(AccountId, UserId, Guid.NewGuid(), Email));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Decrypt_FailsWhenTheLoginIsRewritten()
    {
        var cipher = ConnectedAccountCipher.Encrypt(Kek, "hunter2", Context());

        var result = ConnectedAccountCipher.Decrypt(
            Kek, cipher, ConnectedAccountCipher.Context(AccountId, UserId, DomainId, "mallory@provider.example"));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Decrypt_FailsWhenTheRowIsGivenToAnotherUser()
    {
        var cipher = ConnectedAccountCipher.Encrypt(Kek, "hunter2", Context());

        var result = ConnectedAccountCipher.Decrypt(
            Kek, cipher, ConnectedAccountCipher.Context(AccountId, Guid.NewGuid(), DomainId, Email));

        Assert.True(result.IsFailure);
    }

    /// <summary>A local mailbox carries no domain, and that has to be a context of its own.</summary>
    [Fact]
    public void Decrypt_FailsWhenALocalRowIsGivenADomain()
    {
        var cipher = ConnectedAccountCipher.Encrypt(
            Kek, "hunter2", ConnectedAccountCipher.Context(AccountId, UserId, null, Email));

        var result = ConnectedAccountCipher.Decrypt(Kek, cipher, Context());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Context_ReadsTheFourColumnsOfTheRow()
    {
        var row = new ConnectedAccount
        {
            Id = AccountId, UserId = UserId, DomainId = DomainId, Email = Email
        };

        Assert.Equal<byte[]>(Context(), ConnectedAccountCipher.Context(row));
    }

    // ── Migration off the unbound rows ────────────────────

    [Fact]
    public void Decrypt_StillOpensAPreBindingCipher()
    {
        var legacy = LegacyEncrypt(Kek, "hunter2");

        var result = ConnectedAccountCipher.Decrypt(Kek, legacy, Context(), out var bound);

        Assert.True(result.IsSuccess);
        Assert.Equal("hunter2", result.Value);
        Assert.False(bound);
    }

    [Fact]
    public void Decrypt_ReportsABoundCipherAsBound()
    {
        var cipher = ConnectedAccountCipher.Encrypt(Kek, "hunter2", Context());

        var result = ConnectedAccountCipher.Decrypt(Kek, cipher, Context(), out var bound);

        Assert.True(result.IsSuccess);
        Assert.True(bound);
    }

    /// <summary>
    /// One pre-binding blob in 256 opens on a nonce byte equal to the version marker. Deciding the
    /// shape on that byte alone would refuse those rows outright, so the tag has to be what
    /// decides and a failed bound read must fall through to the unbound one.
    /// </summary>
    [Fact]
    public void Decrypt_OpensAPreBindingCipherWhoseFirstByteLooksLikeTheVersionMarker()
    {
        var nonce = RandomNumberGenerator.GetBytes(ConnectedAccountCipher.NonceLength);
        nonce[0] = 0x02;
        var legacy = LegacyEncrypt(Kek, "hunter2", nonce);
        Assert.Equal(0x02, legacy[0]);

        var result = ConnectedAccountCipher.Decrypt(Kek, legacy, Context(), out var bound);

        Assert.True(result.IsSuccess);
        Assert.Equal("hunter2", result.Value);
        Assert.False(bound);
    }

    /// <summary>
    /// The tolerance is for rows this server wrote, never for the binding: an unbound blob under
    /// the wrong key stays shut, so the fallback cannot be used to launder a tampered row.
    /// </summary>
    [Fact]
    public void Decrypt_FailsOnAPreBindingCipherUnderAnotherKek()
    {
        var otherKek = ConnectedAccountCipher.DeriveKek("a-different-password", ConnectedAccountCipher.NewSalt());
        var legacy = LegacyEncrypt(Kek, "hunter2");

        var result = ConnectedAccountCipher.Decrypt(otherKek, legacy, Context());

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.CredentialsInvalid, result.Error);
    }

    // ── The auth mode ─────────────────────────────────────

    [Fact]
    public void Context_OfAPasswordRow_IsUnchangedFromTheBoundFormat()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var userId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var domainId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var row = new ConnectedAccount
        {
            Id = accountId, UserId = userId, DomainId = domainId,
            Email = "alice@example.test", AuthMode = MailAuthMode.Password
        };

        Assert.Equal(
            $"{accountId:D}|{userId:D}|{domainId:D}|alice@example.test",
            Encoding.UTF8.GetString(ConnectedAccountCipher.Context(row)));
    }

    [Fact]
    public void Context_OfAnOAuthRow_CarriesTheModeBeforeTheAddress()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var userId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var domainId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var row = new ConnectedAccount
        {
            Id = accountId, UserId = userId, DomainId = domainId,
            Email = "alice@example.test", AuthMode = MailAuthMode.OAuth2
        };

        Assert.Equal(
            $"{accountId:D}|{userId:D}|{domainId:D}|oauth2|alice@example.test",
            Encoding.UTF8.GetString(ConnectedAccountCipher.Context(row)));
    }

    [Fact]
    public void Decrypt_UnderTheWrongMode_Fails()
    {
        var kek = ConnectedAccountCipher.DeriveKek("main", ConnectedAccountCipher.NewSalt());
        var row = new ConnectedAccount
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), DomainId = Guid.NewGuid(),
            Email = "alice@example.test", AuthMode = MailAuthMode.OAuth2
        };
        var cipher = ConnectedAccountCipher.Encrypt(kek, "refresh-token", ConnectedAccountCipher.Context(row));

        row.AuthMode = MailAuthMode.Password;

        Assert.True(ConnectedAccountCipher.Decrypt(kek, cipher, ConnectedAccountCipher.Context(row)).IsFailure);
    }

    [Fact]
    public void Encrypt_AcceptsARefreshTokenSizedSecret()
    {
        var kek = ConnectedAccountCipher.DeriveKek("main", ConnectedAccountCipher.NewSalt());
        var context = ConnectedAccountCipher.Context(Guid.NewGuid(), Guid.NewGuid(), null, "a@b.test");
        var secret = new string('t', ConnectedAccountCipher.MaxSecretLength);

        var cipher = ConnectedAccountCipher.Encrypt(kek, secret, context);

        Assert.Equal(secret, ConnectedAccountCipher.Decrypt(kek, cipher, context).Value);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ConnectedAccountCipher.Encrypt(kek, secret + "t", context));
    }
}
