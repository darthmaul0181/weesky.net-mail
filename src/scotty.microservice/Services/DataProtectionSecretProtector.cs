using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace weesky.Scotty.Microservice.Services;

/// <summary>
/// An application secret at rest, under a purpose of its own on the existing key ring. Each secret
/// takes a distinct purpose: they have different lifetimes and blast radii, and a shared purpose
/// would let a blob from one be replayed as the other.
/// </summary>
internal abstract class DataProtectionSecretProtector : ISecretProtector
{
    /// <summary>Plaintext bound that keeps a protected blob well inside the VARBINARY(1024) columns holding them.</summary>
    public const int MaxSecretBytes = 512;

    private readonly IDataProtector _protector;

    protected DataProtectionSecretProtector(IDataProtectionProvider provider, string purpose)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(purpose);
    }

    /// <summary>Null when <paramref name="secret"/> fits under <see cref="MaxSecretBytes"/>; the refusal, naming it, otherwise.</summary>
    public static string? ValidateLength(string? secret, string name) =>
        secret is not null && Encoding.UTF8.GetByteCount(secret) > MaxSecretBytes
            ? $"The {name} may not exceed {MaxSecretBytes} bytes"
            : null;

    public byte[] Protect(string secret) => _protector.Protect(Encoding.UTF8.GetBytes(secret));

    public string? Unprotect(byte[] protectedSecret)
    {
        ArgumentNullException.ThrowIfNull(protectedSecret);
        try
        {
            return Encoding.UTF8.GetString(_protector.Unprotect(protectedSecret));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
