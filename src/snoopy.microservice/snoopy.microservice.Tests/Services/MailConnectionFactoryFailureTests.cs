using System.Net;
using System.Net.Sockets;
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
/// The admin's connection test names why a server refused, so the factory must tell the causes
/// apart. Real sockets: only a live handshake produces the exceptions being classified.
/// </summary>
public sealed class MailConnectionFactoryFailureTests
{
    private static ImapConnectionFactory CreateFactory(int timeoutSeconds = 10, bool allowCleartext = false)
    {
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue)
               .Returns(new MailOptions { TimeoutSeconds = timeoutSeconds, AllowCleartext = allowCleartext });
        return new ImapConnectionFactory(
            monitor.Object, Mock.Of<IMailHtmlSanitizer>(), NullLogger<ImapConnectionFactory>.Instance);
    }

    private static MailAccountConnection To(int port, SecureSocketOptions security) =>
        TestConnections.Primary("alice@weesky.be", "hunter2") with
        {
            ImapHost = "127.0.0.1", ImapPort = port, ImapSecurity = security
        };

    [Fact]
    public async Task AClosedPort_IsUnreachable()
    {
        var result = await CreateFactory().OpenAsync(To(1, SecureSocketOptions.StartTls), CancellationToken.None);

        Assert.Equal(MailConnectionErrors.Unreachable, result.Error);
    }

    [Fact]
    public async Task ATlsHandshakeWithAPlaintextServer_IsASecureChannelFailure()
    {
        using var server = new FakeImapServer();
        server.Start();

        var result = await CreateFactory().OpenAsync(To(server.Port, SecureSocketOptions.SslOnConnect), CancellationToken.None);

        Assert.Equal(MailConnectionErrors.SecureChannelFailed, result.Error);
    }

    [Fact]
    public async Task RefusingACleartextSocket_IsASecureChannelFailure()
    {
        using var server = new FakeImapServer();
        server.Start();

        var result = await CreateFactory().OpenAsync(To(server.Port, SecureSocketOptions.None), CancellationToken.None);

        Assert.Equal(MailConnectionErrors.SecureChannelFailed, result.Error);
    }

    private static SmtpConnectionFactory CreateSmtpFactory(bool allowCleartext = false)
    {
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new MailOptions { TimeoutSeconds = 10, AllowCleartext = allowCleartext });
        return new SmtpConnectionFactory(monitor.Object, NullLogger<SmtpConnectionFactory>.Instance);
    }

    private static MailAccountConnection ToSmtp(int port, SecureSocketOptions security) =>
        TestConnections.Primary("alice@weesky.be", "hunter2") with
        {
            SmtpHost = "127.0.0.1", SmtpPort = port, SmtpSecurity = security
        };

    // The server answered: its missing STARTTLS is a transport-security failure, not an unreachable host.
    [Fact]
    public async Task StartTls_OnAServerWithoutStartTls_IsASecureChannelFailure()
    {
        using var server = new FakeSmtpServer("AUTH PLAIN LOGIN");
        server.Start();

        var result = await CreateSmtpFactory().OpenAsync(ToSmtp(server.Port, SecureSocketOptions.StartTls), CancellationToken.None);

        Assert.Equal(MailConnectionErrors.SecureChannelFailed, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AUTH X-NOBODY-SPEAKS-THIS")]
    public async Task AServerOfferingNoUsableAuthentication_IsAnUnsupportedAuthentication(string extension)
    {
        using var server = new FakeSmtpServer(extension is "" ? [] : [extension]);
        server.Start();

        var result = await CreateSmtpFactory(allowCleartext: true)
            .OpenAsync(ToSmtp(server.Port, SecureSocketOptions.None), CancellationToken.None);

        Assert.Equal(MailConnectionErrors.AuthenticationUnsupported, result.Error);
    }

    [Fact]
    public async Task TheFakeServer_AuthenticatesWhenItOffersAuth()
    {
        using var server = new FakeSmtpServer("AUTH PLAIN LOGIN");
        server.Start();

        var result = await CreateSmtpFactory(allowCleartext: true)
            .OpenAsync(ToSmtp(server.Port, SecureSocketOptions.None), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task AServerThatNeverGreets_TimesOut()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var result = await CreateFactory(timeoutSeconds: 1).OpenAsync(To(port, SecureSocketOptions.None), CancellationToken.None);

            Assert.Equal(MailConnectionErrors.TimedOut, result.Error);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task TheCallersCancellation_StillThrows()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => CreateFactory().OpenAsync(To(port, SecureSocketOptions.None), cts.Token));
        }
        finally
        {
            listener.Stop();
        }
    }
}
