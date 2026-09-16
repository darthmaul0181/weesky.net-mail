namespace weesky.Scotty.Microservice.Services;

/// <summary>Protects one application secret at rest. Each secret has its own interface, and so its own purpose.</summary>
public interface ISecretProtector
{
    byte[] Protect(string secret);

    /// <summary>Null when the blob does not open: a rotated key ring, or a corrupted row.</summary>
    string? Unprotect(byte[] protectedSecret);
}
