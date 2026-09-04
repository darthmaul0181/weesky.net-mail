using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ImapConnectionFactoryClientSourceTests
{
    [Fact]
    public async Task CreateSession_WrapsAClientAndCallsTheGivenReleaseOnDispose()
    {
        using var server = new FakeImapServer();
        server.Start();
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new MailOptions { TimeoutSeconds = 10, AllowCleartext = true });
        IImapClientSource source = new ImapConnectionFactory(
            monitor.Object, Mock.Of<IMailHtmlSanitizer>(), NullLogger<ImapConnectionFactory>.Instance);
        var connection = TestConnections.Primary("alice@weesky.be", "hunter2") with
        {
            ImapHost = "127.0.0.1", ImapPort = server.Port, ImapSecurity = SecureSocketOptions.None
        };
        var opened = await source.OpenClientAsync(connection, CancellationToken.None);
        ImapClient? released = null;

        var session = source.CreateSession(opened.Value, (client, _) => { released = client; return ValueTask.CompletedTask; });
        await session.DisposeAsync();

        Assert.Same(opened.Value, released);
        Assert.True(opened.Value.IsConnected); // the release decides; this one closed nothing
        opened.Value.Dispose();
    }
}
