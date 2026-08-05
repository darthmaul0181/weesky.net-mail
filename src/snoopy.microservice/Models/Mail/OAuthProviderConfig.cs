using System.Diagnostics.CodeAnalysis;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// An OAuth provider an admin fully described. The projection exists so that "is this row
/// usable" is answered once, by the type system, rather than by five null checks spread over
/// the handshake, the refresh and the connect form.
/// </summary>
public sealed record OAuthProviderConfig(
    string AuthorizationUrl, string TokenUrl, string Scopes, string ClientId, byte[] ClientSecret)
{
    /// <summary>
    /// False for a password domain and for an OAuth one missing any of its five fields — the
    /// caller logs it as administrator error and answers account_not_found, exactly as it does
    /// for a domain whose transport security no longer parses.
    /// </summary>
    public static bool TryFrom(ExternalDomain domain, [NotNullWhen(true)] out OAuthProviderConfig? config)
    {
        ArgumentNullException.ThrowIfNull(domain);
        config = null;

        if (domain.AuthMode is not MailAuthMode.OAuth2
            || !IsHttps(domain.OAuthAuthorizationUrl) || !IsHttps(domain.OAuthTokenUrl)
            || string.IsNullOrWhiteSpace(domain.OAuthScopes)
            || string.IsNullOrWhiteSpace(domain.OAuthClientId)
            || domain.OAuthClientSecret is not { Length: > 0 } secret)
            return false;

        config = new OAuthProviderConfig(
            domain.OAuthAuthorizationUrl!, domain.OAuthTokenUrl!, domain.OAuthScopes!,
            domain.OAuthClientId!, secret);
        return true;
    }

    // An endpoint reached in the clear would put the client secret and the refresh token on the
    // wire; there is no AllowCleartext opt-in for this the way there is for IMAP. Internal so the
    // admin write path refuses exactly what this projection would refuse to read back.
    internal static bool IsHttps([NotNullWhen(true)] string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps;
}
