using System.Security.Cryptography;
using System.Text;
using CSharpFunctionalExtensions;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Encrypts connected-account passwords with a key derived from the user's main password —
/// the server alone can never decrypt what it stores. Pure and static: no state, no DI.
/// </summary>
internal static class ConnectedAccountCipher
{
    public const int SaltLength = 16;
    public const int KekIterations = 600_000;

    // connected_accounts.cipher is VARBINARY(512); 512 - NonceLength(12) - TagLength(16) = 484.
    public const int MaxSecretLength = 484;

    internal const int NonceLength = 12;
    internal const int TagLength = 16;

    /// <summary>AES-256: any other length is not a key this cipher can ever have produced.</summary>
    public const int KekLength = 32;

    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltLength);

    public static byte[] DeriveKek(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, KekIterations, HashAlgorithmName.SHA256, KekLength);

    public static byte[] Encrypt(byte[] kek, string secret)
    {
        var plaintext = Encoding.UTF8.GetBytes(secret);
        if (plaintext.Length > MaxSecretLength)
            throw new ArgumentOutOfRangeException(
                nameof(secret), "Secret exceeds the maximum encrypted length.");

        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];

        using var aes = new AesGcm(kek, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var result = new byte[NonceLength + TagLength + ciphertext.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceLength);
        ciphertext.CopyTo(result, NonceLength + TagLength);
        return result;
    }

    public static Result<string> Decrypt(byte[] kek, byte[] cipher)
    {
        if (cipher.Length < NonceLength + TagLength)
            return Result.Failure<string>(ConnectedAccountErrors.CredentialsInvalid);

        var nonce = cipher.AsSpan(0, NonceLength);
        var tag = cipher.AsSpan(NonceLength, TagLength);
        var ciphertext = cipher.AsSpan(NonceLength + TagLength);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(kek, TagLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Result.Success(Encoding.UTF8.GetString(plaintext));
        }
        catch (CryptographicException)
        {
            return Result.Failure<string>(ConnectedAccountErrors.CredentialsInvalid);
        }
    }
}
