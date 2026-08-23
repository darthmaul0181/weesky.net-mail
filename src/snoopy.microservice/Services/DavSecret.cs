using System.Security.Cryptography;
using System.Text;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The synchronisation secret: drawn here, hashed here, compared here, and never stored in clear.
///
/// The digest is a salted SHA-256 and deliberately not a slow KDF. A KDF exists to price the
/// dictionary attack on a secret a human chose; this one carries ~100 bits drawn by us, where an
/// exhaustive search is out of reach at any hashing speed — while a DAV client re-authenticates on
/// every single request, so an iterated KDF here would be a denial of service we inflict on
/// ourselves, triggerable by unauthenticated traffic. See the slice's design note before
/// "correcting" this.
/// </summary>
internal static class DavSecret
{
    /// <summary>20 base32 characters ≈ 100 bits.</summary>
    internal const int Length = 20;

    internal const int SaltLength = 16;

    /// <summary>The width <see cref="Hash"/> writes: a SHA-256 digest in lowercase hex.</summary>
    internal const int HashLength = SHA256.HashSizeInBytes * 2;

    /// <summary>RFC 4648 base32, minus nothing: no whitespace, which is what makes the Trim safe.</summary>
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    internal static string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);

    internal static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltLength);

    internal static string Hash(byte[] salt, string secret) =>
        Convert.ToHexStringLower(SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes(secret)]));

    /// <summary>
    /// Constant-time comparison of the stored digest against the presented secret, whose edge
    /// whitespace is stripped first — copy-paste adds it, the alphabet contains none, and the
    /// symptom without this is a correct password refused.
    /// </summary>
    internal static bool Matches(byte[] salt, string storedHash, string presented)
    {
        var computed = Hash(salt, presented.Trim());
        if (computed.Length != storedHash.Length) return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(storedHash));
    }

    /// <summary>
    /// The variable half of the burst cache's key. Salt-free on purpose: it is never compared to
    /// anything stored, and it exists so the clear secret does not survive the request.
    /// </summary>
    internal static string Fingerprint(string presented) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(presented.Trim())));
}
