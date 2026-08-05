using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// A purpose of its own on the existing key ring, distinct from the credentials cookie's: the two
/// secrets have different lifetimes and different blast radii, and a shared purpose would let a
/// blob from one be replayed as the other.
/// </summary>
internal sealed class ClientSecretProtector : IClientSecretProtector
{
    private const string Purpose = "weesky.oauth.clientsecret";

    private readonly IDataProtector _protector;

    public ClientSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

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
