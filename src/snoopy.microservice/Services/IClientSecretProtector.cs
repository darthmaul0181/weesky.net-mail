namespace weesky.Snoopy.Microservice.Services;

/// <summary>Protects an application secret at rest — the OAuth client secret, and nothing else.</summary>
public interface IClientSecretProtector
{
    byte[] Protect(string secret);

    /// <summary>Null when the blob does not open: a rotated key ring, or a corrupted row.</summary>
    string? Unprotect(byte[] protectedSecret);
}
