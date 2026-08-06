using System.Text.Json;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

public sealed class ExternalDomainResponseTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static ExternalDomainResponse Response() => new(
        Guid.NewGuid(), "outlook", "outlook.office365.com", 993, "SslOnConnect",
        "smtp.office365.com", 587, "StartTls", null, null,
        MailAuthMode.OAuth2, "https://provider.test/authorize", "https://provider.test/token",
        "offline_access", "client-id", OAuthClientSecretSet: true);

    /// <summary>
    /// The camelCase policy stops at the last capital of a run, so these five would ship as
    /// <c>oAuthTokenUrl</c> and the admin dialog — which reads <c>oauthTokenUrl</c> — would edit an
    /// OAuth domain with every provider field blank. Deserialization hides it: it is case-insensitive.
    /// </summary>
    [Theory]
    [InlineData("oauthAuthorizationUrl")]
    [InlineData("oauthTokenUrl")]
    [InlineData("oauthScopes")]
    [InlineData("oauthClientId")]
    [InlineData("oauthClientSecretSet")]
    public void Serialize_NamesTheOAuthFieldsAsTheAdminDialogReadsThem(string property)
    {
        var json = JsonSerializer.Serialize(Response(), Web);

        Assert.Contains($"\"{property}\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_NeverCarriesTheClientSecret()
    {
        var json = JsonSerializer.Serialize(Response(), Web);

        Assert.DoesNotContain("Secret\":\"", json, StringComparison.Ordinal);
        Assert.Contains("\"oauthClientSecretSet\":true", json, StringComparison.Ordinal);
    }
}
