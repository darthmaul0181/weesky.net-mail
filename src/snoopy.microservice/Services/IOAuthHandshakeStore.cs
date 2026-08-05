using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The consents in flight. Process-local and never persisted: the tokens it briefly holds have no
/// rest to be encrypted at, and a restart mid-handshake costs one restarted consent.
/// </summary>
public interface IOAuthHandshakeStore
{
    OAuthHandshake Start(Guid userId, Guid domainId, Guid? accountId);

    OAuthHandshake? Find(string state);

    /// <summary>False when the state is unknown or expired.</summary>
    bool Attach(string state, OAuthTokenResponse tokens, string email);

    /// <summary>
    /// Removes and answers the handshake, but only for the user who started it — the check
    /// without which one user could complete another's consent. A mismatch leaves the entry.
    /// </summary>
    OAuthHandshake? Consume(string state, Guid userId);
}
