using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The only place this application talks to an identity provider's token endpoint.
/// Failures are <see cref="ConnectedAccountErrors.CredentialsInvalid"/> (the user must consent
/// again) or <see cref="ConnectedAccountErrors.ProviderUnavailable"/> (try later).
/// </summary>
public interface IOAuthTokenService
{
    /// <summary>A live access token for this row, from cache when one is still good.</summary>
    Task<Result<string>> GetAccessTokenAsync(
        ConnectedAccount row, OAuthProviderConfig provider, byte[] kek, CancellationToken cancellationToken);

    /// <summary>The authorization-code half of the handshake. No row exists yet.</summary>
    Task<Result<OAuthTokenResponse>> ExchangeCodeAsync(
        OAuthProviderConfig provider, string code, string codeVerifier, string redirectUri,
        CancellationToken cancellationToken);
}
