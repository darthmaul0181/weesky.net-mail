using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace weesky.Scotty.Microservice.Services.Calendar.Delivery;

/// <summary>256 random bits, shown once, kept as an unsalted SHA-256: a dictionary means nothing
/// against a random key, and the compare is constant-time (spec 5e3, décision 6).</summary>
internal static class DeliveryKeys
{
    internal const int KeyBytes = 32;

    internal static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(KeyBytes));

    internal static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    internal static bool Matches(string? presented, byte[] storedHash) =>
        !string.IsNullOrEmpty(presented) && CryptographicOperations.FixedTimeEquals(Hash(presented), storedHash);
}
