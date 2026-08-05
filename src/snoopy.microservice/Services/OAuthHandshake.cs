using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// One consent in flight. It exists because the provider's redirect carries no cookie —
/// SameSite=Strict — so the request that brings the code back can neither identify the user nor
/// derive their key; this is what carries both across the three steps.
/// <see cref="AccountId"/> is set when re-authenticating an existing row rather than attaching one.
/// </summary>
public sealed record OAuthHandshake(
    string State,
    Guid UserId,
    Guid DomainId,
    Guid? AccountId,
    string CodeVerifier,
    string CodeChallenge,
    OAuthTokenResponse? Tokens,
    string? Email)
{
    /// <summary>Redacted: the generated ToString would print the state, the PKCE verifier and,
    /// through <see cref="Tokens"/>, the live tokens into any log line.</summary>
    public override string ToString() => $"OAuthHandshake ({UserId}, domain={DomainId})";
}
