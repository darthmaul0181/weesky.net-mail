using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The baseline the pool will be judged against: every open logs how long connect+TLS and
/// AUTHENTICATE took, before any pooling exists to muddy the comparison.
/// </summary>
public sealed class MailConnectionFactoryTimingTests
{
    [Fact]
    public async Task OpenClientAsync_LogsConnectAndAuthenticateDurations()
    {
        using var server = new FakeImapServer();
        server.Start();
        var logger = new Mock<ILogger<ImapConnectionFactory>>();
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new MailOptions { TimeoutSeconds = 10, AllowCleartext = true });
        var factory = new ImapConnectionFactory(monitor.Object, Mock.Of<IMailHtmlSanitizer>(), logger.Object);
        var connection = TestConnections.Primary("alice@weesky.be", "hunter2") with
        {
            ImapHost = "127.0.0.1", ImapPort = server.Port, ImapSecurity = SecureSocketOptions.None
        };

        var opened = await factory.OpenClientAsync(connection, CancellationToken.None);

        Assert.True(opened.IsSuccess);
        Assert.True(opened.Value.IsAuthenticated);
        logger.Verify(l => l.Log(
                LogLevel.Debug, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("authenticate")),
                null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        opened.Value.Dispose();
    }
}
