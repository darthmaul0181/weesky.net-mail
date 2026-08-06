using System.Text.Json.Serialization;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// An admin-curated external mail provider, as the settings UI sees it. The client secret has no
/// shape in which it may leave this service: <c>OAuthClientSecretSet</c> is all a reader learns.
///
/// The five OAuth names are spelled out because the camelCase policy stops at the last capital of
/// a run and would ship <c>oAuthTokenUrl</c>, which no client reads. Deserialization hides the
/// mismatch — it is case-insensitive — so only reading back breaks.
/// </summary>
public sealed record ExternalDomainResponse(
    Guid Id, string Name, string ImapHost, int ImapPort, string ImapSecurity,
    string SmtpHost, int SmtpPort, string SmtpSecurity, string? SieveHost, int? SievePort,
    MailAuthMode AuthMode,
    [property: JsonPropertyName("oauthAuthorizationUrl")] string? OAuthAuthorizationUrl,
    [property: JsonPropertyName("oauthTokenUrl")] string? OAuthTokenUrl,
    [property: JsonPropertyName("oauthScopes")] string? OAuthScopes,
    [property: JsonPropertyName("oauthClientId")] string? OAuthClientId,
    [property: JsonPropertyName("oauthClientSecretSet")] bool OAuthClientSecretSet);

/// <summary>
/// Registers or edits an external mail provider. <c>ImapSecurity</c>/<c>SmtpSecurity</c> must be
/// exactly one of <c>None</c>, <c>StartTls</c>, <c>SslOnConnect</c> — case-sensitive, no numeric
/// form accepted — since the resolver that later reads the stored value is case-sensitive too.
/// <c>AuthMode</c> follows the same rule: exactly <c>Password</c> (what null means) or
/// <c>OAuth2</c>. The client secret is write-only — plaintext in, protected at rest, never
/// echoed by any endpoint; left empty on an edit it keeps the stored one.
/// </summary>
public sealed record ExternalDomainRequest(
    string Name, string ImapHost, int ImapPort, string ImapSecurity,
    string SmtpHost, int SmtpPort, string SmtpSecurity, string? SieveHost, int? SievePort,
    string? AuthMode = null, string? OAuthAuthorizationUrl = null, string? OAuthTokenUrl = null,
    string? OAuthScopes = null, string? OAuthClientId = null, string? OAuthClientSecret = null)
{
    /// <summary>Redacted: the generated ToString would print the client secret into any log line.</summary>
    public override string ToString() => $"ExternalDomainRequest ({Name})";
}
