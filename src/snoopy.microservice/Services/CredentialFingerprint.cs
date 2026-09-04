using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// What the pool indexes a credential by, so that no table ever holds a password. HMAC-SHA256 under
/// a key drawn at startup and never persisted. The kind and the secret are length-delimited so two
/// distinct pairs cannot concatenate to the same bytes.
/// </summary>
internal sealed class CredentialFingerprint
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public string Of(MailCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        var (kind, secret) = credential switch
        {
            PasswordCredential password => ("password", password.Password),
            OAuthCredential oauth => ("oauth", oauth.AccessToken),
            _ => throw new UnreachableException()
        };

        var kindBytes = Encoding.UTF8.GetBytes(kind);
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var input = new byte[8 + kindBytes.Length + secretBytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(input, kindBytes.Length);
        kindBytes.CopyTo(input, 4);
        BinaryPrimitives.WriteInt32BigEndian(input.AsSpan(4 + kindBytes.Length), secretBytes.Length);
        secretBytes.CopyTo(input, 8 + kindBytes.Length);

        try
        {
            return Convert.ToBase64String(HMACSHA256.HashData(_key, input));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }
}
