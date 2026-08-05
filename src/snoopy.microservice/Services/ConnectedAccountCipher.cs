using System.Security.Cryptography;
using System.Text;
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Encrypts connected-account passwords with a key derived from the user's main password —
/// the server alone can never decrypt what it stores. Pure and static: no state, no DI.
///
/// Each ciphertext is additionally bound to the row it belongs to — see
/// <see cref="Context(Guid, Guid, Guid?, string, MailAuthMode)"/>. The key
/// is per-user, not per-account, so without that binding write access to the database is enough to
/// point a row at another host — or to move one account's cipher onto another's row — and have the
/// server hand that host the password it faithfully decrypted. SASL PLAIN sends the credentials
/// before the server answers, and TLS does not help: the attacker's own certificate is valid.
/// The binding protects the secret's destination, never the secret itself.
/// </summary>
internal static class ConnectedAccountCipher
{
    public const int SaltLength = 16;
    public const int KekIterations = 600_000;

    internal const int NonceLength = 12;
    internal const int TagLength = 16;

    /// <summary>Marks a blob carrying associated data; a pre-binding one opens straight on its nonce.</summary>
    private const byte BoundVersion = 0x02;

    // connected_accounts.cipher is VARBINARY(8192); 8192 - 1 (version) - 12 (nonce) - 16 (tag) = 8163.
    // Not a precaution: a Microsoft refresh token is an encrypted blob that routinely exceeds 1 KB.
    public const int MaxSecretLength = 8163;

    /// <summary>AES-256: any other length is not a key this cipher can ever have produced.</summary>
    public const int KekLength = 32;

    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltLength);

    public static byte[] DeriveKek(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, KekIterations, HashAlgorithmName.SHA256, KekLength);

    /// <summary>
    /// Everything that decides where a secret is sent and as whom: the row's own identity, its
    /// owner, the external domain carrying the host, and the login. Repointing any of them breaks
    /// the tag instead of redirecting the password. None changes in the life of a row — no endpoint
    /// updates an address or a domain — so binding all four costs nothing operationally.
    ///
    /// The guids cannot contain the separator and the address comes last, so no two different
    /// rows can produce the same context.
    ///
    /// The mode segment is written only for OAuth, and before the address: every row bound before
    /// the mode existed still opens, and the address stays last so no two rows can collide.
    /// </summary>
    public static byte[] Context(
        Guid accountId, Guid userId, Guid? domainId, string email,
        MailAuthMode authMode = MailAuthMode.Password) =>
        Encoding.UTF8.GetBytes(
            $"{accountId:D}|{userId:D}|{domainId?.ToString("D") ?? string.Empty}|"
            + (authMode is MailAuthMode.OAuth2 ? "oauth2|" : string.Empty)
            + email);

    /// <summary>
    /// The context of a row — one definition, so the five call sites cannot drift. The address is
    /// canonicalised here because the store canonicalises what it writes: a context built on the
    /// raw spelling at creation time would bind the cipher to an address the row never holds, and
    /// it would never open again.
    /// </summary>
    public static byte[] Context(ConnectedAccount row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Context(row.Id, row.UserId, row.DomainId, IdentityResolver.Canonical(row.Email), row.AuthMode);
    }

    public static byte[] Encrypt(byte[] kek, string secret, byte[] context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var plaintext = Encoding.UTF8.GetBytes(secret);
        if (plaintext.Length > MaxSecretLength)
            throw new ArgumentOutOfRangeException(
                nameof(secret), "Secret exceeds the maximum encrypted length.");

        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];

        using var aes = new AesGcm(kek, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, context);

        var result = new byte[1 + NonceLength + TagLength + ciphertext.Length];
        result[0] = BoundVersion;
        nonce.CopyTo(result, 1);
        tag.CopyTo(result, 1 + NonceLength);
        ciphertext.CopyTo(result, 1 + NonceLength + TagLength);
        return result;
    }

    public static Result<string> Decrypt(byte[] kek, byte[] cipher, byte[] context) =>
        Decrypt(kek, cipher, context, out _);

    /// <summary>
    /// <paramref name="bound"/> reports which shape opened: false means a pre-binding row, which
    /// the caller may rewrite now that it holds the plaintext.
    /// </summary>
    public static Result<string> Decrypt(byte[] kek, byte[] cipher, byte[] context, out bool bound)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(context);

        bound = false;

        // The version marker is a hint, never a decision: a pre-binding blob opens on a random
        // nonce byte, which is 0x02 once in 256. The tag is what actually tells the two shapes
        // apart, so a failed read of one falls through to the other rather than refusing a row
        // that is perfectly good.
        if (cipher.Length > 0 && cipher[0] == BoundVersion
            && TryOpen(kek, cipher.AsSpan(1), context, out var secret))
        {
            bound = true;
            return Result.Success(secret);
        }

        // Rows written before the binding existed. Still readable on purpose: refusing them would
        // lock every user out of every connected mailbox on the deploy that shipped this, and the
        // provider passwords are not ours to ask for again.
        return TryOpen(kek, cipher, ReadOnlySpan<byte>.Empty, out var legacy)
            ? Result.Success(legacy)
            : Result.Failure<string>(ConnectedAccountErrors.CredentialsInvalid);
    }

    /// <summary>True when the blob authenticates under this key and this associated data.</summary>
    private static bool TryOpen(
        byte[] kek, ReadOnlySpan<byte> blob, ReadOnlySpan<byte> associatedData, out string secret)
    {
        secret = string.Empty;
        if (blob.Length < NonceLength + TagLength) return false;

        var plaintext = new byte[blob.Length - NonceLength - TagLength];
        try
        {
            using var aes = new AesGcm(kek, TagLength);
            aes.Decrypt(
                blob[..NonceLength],
                blob[(NonceLength + TagLength)..],
                blob.Slice(NonceLength, TagLength),
                plaintext,
                associatedData);
        }
        catch (CryptographicException)
        {
            return false;
        }

        secret = Encoding.UTF8.GetString(plaintext);
        return true;
    }
}
