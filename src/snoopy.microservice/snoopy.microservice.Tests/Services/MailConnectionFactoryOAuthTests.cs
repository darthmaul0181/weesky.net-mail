using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The credential decides the SASL mechanism, and only a real dialogue can pin that down: a mock
/// would assert the call this code makes rather than the command the server receives.
/// </summary>
public sealed class MailConnectionFactoryOAuthTests
{
    private static ImapConnectionFactory CreateFactory()
    {
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue)
               .Returns(new MailOptions { TimeoutSeconds = 10, AllowCleartext = true });

        return new ImapConnectionFactory(
            monitor.Object, Mock.Of<IMailHtmlSanitizer>(), NullLogger<ImapConnectionFactory>.Instance);
    }

    private static MailAccountConnection On(int port, MailCredential credential) =>
        TestConnections.Primary("alice@weesky.be", credential) with
        {
            ImapHost = "127.0.0.1", ImapPort = port, ImapSecurity = SecureSocketOptions.None
        };

    [Fact]
    public async Task OpenAsync_AuthenticatesAnOAuthCredentialOverXOAuth2()
    {
        using var server = new FakeImapServer(oauth: true);
        server.Start();

        var result = await CreateFactory()
            .OpenAsync(On(server.Port, new OAuthCredential("ya29.token")), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("XOAUTH2", server.AuthenticateMechanism);
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task OpenAsync_AuthenticatesAPasswordCredentialWithoutSasl()
    {
        using var server = new FakeImapServer();
        server.Start();

        var result = await CreateFactory()
            .OpenAsync(On(server.Port, new PasswordCredential("hunter2")), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(server.AuthenticateMechanism);
        await result.Value.DisposeAsync();
    }

    [Fact]
    public void ToString_OfACredentialNeverPrintsTheSecret()
    {
        Assert.DoesNotContain("hunter2", new PasswordCredential("hunter2").ToString());
        Assert.DoesNotContain("ya29.token", new OAuthCredential("ya29.token").ToString());
    }
}
