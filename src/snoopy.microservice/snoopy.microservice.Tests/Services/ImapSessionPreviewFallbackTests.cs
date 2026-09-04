using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// A server without PREVIEW (RFC 8970) gets its previews emulated by MailKit as partial body
/// fetches, and InterMail (Proximus) answers NO to those for some messages. A preview is
/// decorative: the page must come back without it, on a socket still fit for the pool.
/// </summary>
public sealed class ImapSessionPreviewFallbackTests
{
    private static async Task<(ImapSession Session, ImapClient Client, Mock<ILogger> Logger)> OpenAsync(
        SummaryImapServer server, CancellationToken cancellationToken)
    {
        var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", server.Port, SecureSocketOptions.None, cancellationToken);
        await client.AuthenticateAsync("alice", "hunter2", cancellationToken);
        var logger = new Mock<ILogger>();
        return (new ImapSession(client, Mock.Of<IMailHtmlSanitizer>(), logger.Object), client, logger);
    }

    [Fact]
    public async Task ListMessages_WhenTheServerRefusesThePreviewFetch_ListsWithoutPreviews()
    {
        using var server = new SummaryImapServer(refusePreview: true);
        server.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (session, client, logger) = await OpenAsync(server, cts.Token);
        await using var _ = session;

        var result = await session.ListMessagesAsync("INBOX", 0, 50, grouped: false, cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        var message = Assert.Single(result.Value.Messages);
        Assert.Equal("Plain", message.Subject);
        Assert.Equal(string.Empty, message.Preview);
        Assert.False(session.Tainted);
        Assert.True(client.IsConnected);
        Assert.Equal(1, server.FetchCommands.Count(c => c.Contains("<0.", StringComparison.Ordinal)));
        Assert.Equal(2, server.FetchCommands.Count(c => c.Contains("ENVELOPE", StringComparison.Ordinal)));
        logger.Verify(l => l.Log(
                LogLevel.Warning, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("INBOX")),
                null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ListMessages_WhenTheServerServesThePreview_FetchesOnce()
    {
        using var server = new SummaryImapServer(refusePreview: false);
        server.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (session, _, logger) = await OpenAsync(server, cts.Token);
        await using var __ = session;

        var result = await session.ListMessagesAsync("INBOX", 0, 50, grouped: false, cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal("Hello there", Assert.Single(result.Value.Messages).Preview);
        Assert.Equal(1, server.FetchCommands.Count(c => c.Contains("ENVELOPE", StringComparison.Ordinal)));
        logger.Verify(l => l.Log(
                LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}

/// <summary>
/// One single-part message, no SORT/THREAD (so the list takes the sequence-window path), and a
/// preview fetch — any partial body fetch, "&lt;0." on the wire — the server either serves or
/// refuses with InterMail's own words.
/// </summary>
internal sealed class SummaryImapServer(bool refusePreview) : IDisposable
{
    private const string Caps = "IMAP4rev1 NAMESPACE";
    private const string Body = "Hello there";

    private const string Envelope =
        "(\"Sat, 01 Aug 2026 10:00:00 +0000\" \"Plain\" ((\"Alice\" NIL \"alice\" \"weesky.be\")) " +
        "((\"Alice\" NIL \"alice\" \"weesky.be\")) ((\"Alice\" NIL \"alice\" \"weesky.be\")) " +
        "((\"Bob\" NIL \"bob\" \"weesky.be\")) NIL NIL NIL \"<p@weesky.be>\")";

    private const string BodyStructure =
        "(\"TEXT\" \"PLAIN\" (\"CHARSET\" \"us-ascii\") NIL NIL \"7BIT\" 11 1 NIL NIL NIL NIL)";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Raw FETCH command lines, so a test can count what was actually requested.</summary>
    public List<string> FetchCommands { get; } = [];

    public void Start()
    {
        _listener.Start();
        _ = ServeAsync();
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

                var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length < 2) continue;
                var tag = words[0];
                var command = words[1].ToUpperInvariant();
                if (command == "UID" && words.Length > 2) command = words[2].ToUpperInvariant();

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

                    case "LIST":
                        await writer.WriteLineAsync("* LIST (\\HasNoChildren) \"/\" \"INBOX\"");
                        await writer.WriteLineAsync($"{tag} OK LIST completed");
                        break;

                    case "SELECT":
                    case "EXAMINE":
                        await writer.WriteLineAsync("* 1 EXISTS");
                        await writer.WriteLineAsync("* 0 RECENT");
                        await writer.WriteLineAsync("* FLAGS (\\Seen)");
                        await writer.WriteLineAsync("* OK [UIDVALIDITY 1] UIDs valid");
                        await writer.WriteLineAsync("* OK [UIDNEXT 2] Predicted next UID");
                        await writer.WriteLineAsync($"{tag} OK [READ-ONLY] {command} completed");
                        break;

                    case "FETCH":
                        FetchCommands.Add(line);
                        await RespondToFetchAsync(writer, tag, line);
                        break;

                    case "LOGOUT":
                        await writer.WriteLineAsync("* BYE logging out");
                        await writer.WriteLineAsync($"{tag} OK LOGOUT completed");
                        return;

                    default:
                        await writer.WriteLineAsync($"{tag} BAD unhandled command in fake server: {command}");
                        break;
                }
            }
        }
        catch (Exception)
        {
            // The test's own assertions are the source of truth; a torn-down connection is not a failure.
        }
    }

    private async Task RespondToFetchAsync(StreamWriter writer, string tag, string line)
    {
        if (line.Contains("<0.", StringComparison.Ordinal))
        {
            if (refusePreview)
            {
                await writer.WriteLineAsync($"{tag} NO FETCH could not complete for one or more messages");
                return;
            }

            var section = Regex.Match(line, @"BODY\.PEEK\[([^\]]*)\]").Groups[1].Value;
            await writer.WriteLineAsync($"* 1 FETCH (UID 1 BODY[{section}]<0> {{{Body.Length}}}");
            await writer.WriteAsync(Body);
            await writer.WriteLineAsync(")");
            await writer.WriteLineAsync($"{tag} OK FETCH completed");
            return;
        }

        var fields = Regex.Match(line, @"HEADER\.FIELDS \(([^)]*)\)").Groups[1].Value;
        await writer.WriteLineAsync(
            $"* 1 FETCH (UID 1 FLAGS (\\Seen) RFC822.SIZE 120 INTERNALDATE \"01-Aug-2026 10:00:00 +0000\" " +
            $"ENVELOPE {Envelope} BODYSTRUCTURE {BodyStructure} BODY[HEADER.FIELDS ({fields})] {{2}}");
        await writer.WriteAsync("\r\n");
        await writer.WriteLineAsync(")");
        await writer.WriteLineAsync($"{tag} OK FETCH completed");
    }

    public void Dispose() => _listener.Stop();
}
