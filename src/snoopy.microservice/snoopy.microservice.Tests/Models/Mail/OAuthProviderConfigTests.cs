using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models.Mail;

public sealed class OAuthProviderConfigTests
{
    private static ExternalDomain Complete() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Outlook",
        AuthMode = MailAuthMode.OAuth2,
        OAuthAuthorizationUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
        OAuthTokenUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
        OAuthScopes = "offline_access openid email",
        OAuthClientId = "client-id",
        OAuthClientSecret = [1, 2, 3]
    };

    [Fact]
    public void TryFrom_ReadsACompleteRow()
    {
        Assert.True(OAuthProviderConfig.TryFrom(Complete(), out var config));
        Assert.Equal("client-id", config!.ClientId);
        Assert.Equal("offline_access openid email", config.Scopes);
    }

    [Fact]
    public void TryFrom_RefusesAPasswordDomain()
    {
        var domain = Complete();
        domain.AuthMode = MailAuthMode.Password;

        Assert.False(OAuthProviderConfig.TryFrom(domain, out var config));
        Assert.Null(config);
    }

    [Theory]
    [InlineData("OAuthAuthorizationUrl")]
    [InlineData("OAuthTokenUrl")]
    [InlineData("OAuthScopes")]
    [InlineData("OAuthClientId")]
    public void TryFrom_RefusesARowMissingAnyStringField(string missing)
    {
        var domain = Complete();
        typeof(ExternalDomain).GetProperty(missing)!.SetValue(domain, null);

        Assert.False(OAuthProviderConfig.TryFrom(domain, out _));
    }

    [Fact]
    public void TryFrom_RefusesARowWithNoClientSecret()
    {
        var domain = Complete();
        domain.OAuthClientSecret = null;

        Assert.False(OAuthProviderConfig.TryFrom(domain, out _));
    }

    [Fact]
    public void TryFrom_RefusesANonHttpsEndpoint()
    {
        var domain = Complete();
        domain.OAuthTokenUrl = "http://login.microsoftonline.com/common/oauth2/v2.0/token";

        Assert.False(OAuthProviderConfig.TryFrom(domain, out _));
    }
}
