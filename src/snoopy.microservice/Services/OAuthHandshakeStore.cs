using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

internal sealed class OAuthHandshakeStore(IMemoryCache cache) : IOAuthHandshakeStore
{
    /// <summary>Long enough for a real sign-in with a second factor, short enough that an
    /// abandoned consent is not a live entry an hour later.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public OAuthHandshake Start(Guid userId, Guid domainId, Guid? accountId)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var handshake = new OAuthHandshake(
            State: Base64Url(RandomNumberGenerator.GetBytes(16)),
            UserId: userId,
            DomainId: domainId,
            AccountId: accountId,
            CodeVerifier: verifier,
            CodeChallenge: Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            Tokens: null,
            Email: null);

        cache.Set(Key(handshake.State), handshake, Lifetime);
        return handshake;
    }

    public OAuthHandshake? Find(string state) =>
        string.IsNullOrEmpty(state) ? null : cache.Get<OAuthHandshake>(Key(state));

    public bool Attach(string state, OAuthTokenResponse tokens, string email)
    {
        if (Find(state) is not { } handshake) return false;

        cache.Set(Key(state), handshake with { Tokens = tokens, Email = email }, Lifetime);
        return true;
    }

    public OAuthHandshake? Consume(string state, Guid userId)
    {
        if (Find(state) is not { } handshake || handshake.UserId != userId) return null;

        cache.Remove(Key(state));
        return handshake;
    }

    private static string Key(string state) => $"oauth-handshake:{state}";

    /// <summary>RFC 7636 requires base64url without padding for the challenge.</summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
