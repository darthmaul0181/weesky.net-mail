using System.Text.Json.Serialization;

namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>The fields of an RFC 6749 token response this application reads. Everything else the
/// provider sends is ignored on purpose.</summary>
public sealed record OAuthTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresInSeconds,
    [property: JsonPropertyName("id_token")] string? IdToken)
{
    /// <summary>Redacted: the generated ToString would print both tokens into any log line.</summary>
    public override string ToString() => $"OAuthTokenResponse (expiresIn={ExpiresInSeconds}s)";
}
