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
/// The cleartext gate is decided from the connected client, never from the configured value:
/// Auto and StartTlsWhenAvailable negotiate, so a server that drops STARTTLS — or an attacker
/// stripping it from the pre-auth banner — reaches the factory with the configuration intact.
/// Only a real connection to a real (if disposable) server can pin that down, which is why these
/// drive FakeImapServer rather than a mock.
/// </summary>
public sealed class MailConnectionFactoryCleartextTests
{
    private static ImapConnectionFactory CreateFactory(bool allowCleartext)
    {
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue)
               .Returns(new MailOptions { TimeoutSeconds = 10, AllowCleartext = allowCleartext });

        return new ImapConnectionFactory(
            monitor.Object, Mock.Of<IMailHtmlSanitizer>(), NullLogger<ImapConnectionFactory>.Instance);
    }

    private static MailAccountConnection Cleartext(int port) =>
        TestConnections.Primary("alice@weesky.be", "hunter2") with
        {
            ImapHost = "127.0.0.1", ImapPort = port, ImapSecurity = SecureSocketOptions.None
        };

    [Fact]
    public async Task OpenAsync_RefusesToAuthenticateOverAnUnencryptedConnection()
    {
        using var server = new FakeImapServer();
        server.Start();

        var result = await CreateFactory(allowCleartext: false).OpenAsync(Cleartext(server.Port), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task OpenAsync_AllowsAnUnencryptedConnectionUnderTheOptIn()
    {
        using var server = new FakeImapServer();
        server.Start();

        var result = await CreateFactory(allowCleartext: true).OpenAsync(Cleartext(server.Port), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await result.Value.DisposeAsync();
    }
}
