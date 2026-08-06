using System.Net;
using System.Net.Sockets;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class SmtpSessionTests
{
    private static MimeMessage MessageFrom(string address)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("X", address));
        return message;
    }

    [Fact]
    public void DescribeFailure_NamesTheSenderOnASenderRejection()
    {
        var ex = new SmtpCommandException(SmtpErrorCode.SenderNotAccepted,
            SmtpStatusCode.MailboxNameNotAllowed, "denied");

        Assert.Equal("The mail server refused to send from michel@weesky.be",
            SmtpSession.DescribeFailure(ex, MessageFrom("michel@weesky.be")));
    }

    [Fact]
    public void DescribeFailure_StaysGenericForAnythingElse()
    {
        Assert.Equal("The mail server refused the message",
            SmtpSession.DescribeFailure(new InvalidOperationException("boom"), MessageFrom("a@b.c")));
    }

    [Fact]
    public void Constructor_RejectsANullClient() =>
        Assert.Throws<ArgumentNullException>(() => new SmtpSession(null!, NullLogger.Instance));

    [Fact]
    public void Constructor_RejectsANullLogger() =>
        Assert.Throws<ArgumentNullException>(() => new SmtpSession(new SmtpClient(), null!));

    /// <summary>
    /// Disposal happens after the response is already produced, so a QUIT round trip a half-dead
    /// peer never answers must not be what the user waits on.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_WhenThePeerNeverAnswersTheQuit_StillCompletes()
    {
        using var server = new MuteAfterGreetingSmtpServer();
        using var client = new SmtpClient();
        using var connect = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync("127.0.0.1", server.Port, SecureSocketOptions.None, connect.Token);

        var session = new SmtpSession(client, NullLogger.Instance);

        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task DisposeAsync_OnAClientThatNeverConnected_Completes()
    {
        var session = new SmtpSession(new SmtpClient(), NullLogger.Instance);

        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
    }

    /// <summary>Greets, answers the EHLO, then never says another word — not even to QUIT.</summary>
    private sealed class MuteAfterGreetingSmtpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();

        public MuteAfterGreetingSmtpServer()
        {
            _listener.Start();
            _ = ServeAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        private async Task ServeAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await using var stream = client.GetStream();
                await stream.WriteAsync(Encoding.ASCII.GetBytes("220 fake ESMTP\r\n"), _stop.Token);
                await ReadLineAsync(stream);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("250-fake\r\n250 HELP\r\n"), _stop.Token);
                await Task.Delay(Timeout.Infinite, _stop.Token);
            }
            catch { /* the fixture is being torn down */ }
        }

        private async Task ReadLineAsync(Stream stream)
        {
            var one = new byte[1];
            while (await stream.ReadAsync(one, _stop.Token) != 0)
            {
                if (one[0] == (byte)'\n') return;
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Dispose();
            _stop.Dispose();
        }
    }
}
