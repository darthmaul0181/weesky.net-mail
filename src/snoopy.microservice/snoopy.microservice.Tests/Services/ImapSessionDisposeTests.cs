using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// DisposeAsync runs at request-scope teardown, after the response has been produced: a polite
/// LOGOUT is worth milliseconds on a live connection, but on a half-dead one an uncapped
/// DisconnectAsync inherits the protocol timeout and stalls disposal for its full length. The
/// cap has to come from a token of DisposeAsync's own — no request token exists there.
/// </summary>
public sealed class ImapSessionDisposeTests
{
    [Fact]
    public async Task DisposeAsync_IsBoundedWhenTheServerNeverAnswersLogout()
    {
        using var server = new SilentLogoutImapServer();
        server.Start();

        var client = new ImapClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync("127.0.0.1", server.Port, SecureSocketOptions.None, cts.Token);
        await client.AuthenticateAsync("alice", "hunter2", cts.Token);

        // The protocol timeout a live deployment sets from MailOptions (30 s in appsettings);
        // shortened so the pre-fix behaviour — disposal stalling for its full length — fails
        // this test in seconds instead of half a minute.
        client.Timeout = 15_000;

        var session = new ImapSession(client, Mock.Of<IMailHtmlSanitizer>(), Mock.Of<ILogger>());

        var stopwatch = Stopwatch.StartNew();
        await session.DisposeAsync();
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8),
            $"DisposeAsync took {stopwatch.Elapsed.TotalSeconds:F1}s on a dead connection — teardown must be capped well under the protocol timeout.");
    }
}

/// <summary>
/// Takes an ImapClient through Connect and Authenticate, then swallows LOGOUT without ever
/// answering — the half-dead connection DisposeAsync must not wait out.
/// </summary>
internal sealed class SilentLogoutImapServer : IDisposable
{
    private const string Caps = "IMAP4rev1 NAMESPACE";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private Task? _serverLoop;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start()
    {
        _listener.Start();
        _serverLoop = ServeAsync();
    }

    private async Task ServeAsync()
    {
        try
        {
            using var tcpClient = await _listener.AcceptTcpClientAsync();
            using var stream = tcpClient.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

            await writer.WriteLineAsync($"* OK [CAPABILITY {Caps}] Fake IMAP ready");

            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line is null) return;

                var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                var tag = parts[0];
                var command = parts[1].ToUpperInvariant();

                switch (command)
                {
                    case "LOGIN":
                        await writer.WriteLineAsync($"{tag} OK [CAPABILITY {Caps}] LOGIN completed");
                        break;

                    case "CAPABILITY":
                        await writer.WriteLineAsync($"* CAPABILITY {Caps}");
                        await writer.WriteLineAsync($"{tag} OK CAPABILITY completed");
                        break;

                    case "NAMESPACE":
                        await writer.WriteLineAsync("* NAMESPACE ((\"\" \"/\")) NIL NIL");
                        await writer.WriteLineAsync($"{tag} OK NAMESPACE completed");
                        break;

                    // The half-dead server: the command is read, the answer never comes and the
                    // socket stays open, so only the caller's own bound can end the wait.
                    case "LOGOUT":
                        break;

                    default:
                        await writer.WriteLineAsync($"{tag} BAD unhandled command in fake server: {command}");
                        break;
                }
            }
        }
        catch (Exception)
        {
            // The test's own assertions are the source of truth; a torn-down connection is not a
            // failure this loop needs to surface.
        }
    }

    public void Dispose() => _listener.Stop();
}
