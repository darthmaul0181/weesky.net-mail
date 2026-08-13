using System.Net;
using System.Net.Sockets;
using System.Text;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// Closes the gap the class doc comment on ExecuteAsync used to flag: SearchAsync,
/// ListMessagesAsync, GetMessageAsync and GetAttachmentAsync used to pass no sentinel at all,
/// so a folder deleted from another client mid-session fell through to the opaque failure
/// message instead of the shared 404 sentinel every other folder-scoped operation already
/// answers with. Same seam as ImapSessionListFoldersTests: ImapSession wraps a concrete
/// MailKit ImapClient rather than an interface, so driving a real FolderNotFoundException
/// needs a real (if disposable) server — here scripted to answer every LIST with zero
/// matches, so GetFolderAsync always throws it.
/// </summary>
public sealed class ImapSessionFolderNotFoundTests
{
    private static async Task<ImapSession> ConnectedSessionAsync(NoSuchFolderImapServer server, CancellationToken token)
    {
        var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", server.Port, SecureSocketOptions.None, token);
        await client.AuthenticateAsync("alice", "hunter2", token);
        return new ImapSession(client, Mock.Of<IMailHtmlSanitizer>(), Mock.Of<ILogger>());
    }

    [Fact]
    public async Task SearchAsync_MapsAVanishedFolderToTheFolderSentinel()
    {
        using var server = new NoSuchFolderImapServer();
        server.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = await ConnectedSessionAsync(server, cts.Token);

        var criteria = new MailSearchCriteria("hello", null, null, null, null, null, false, false, false);
        var result = await session.SearchAsync("Gone", allFolders: false, criteria, 0, 50, cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ImapSession.FolderNotFound, result.Error);
    }

    [Fact]
    public async Task ListMessagesAsync_MapsAVanishedFolderToTheFolderSentinel()
    {
        using var server = new NoSuchFolderImapServer();
        server.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = await ConnectedSessionAsync(server, cts.Token);

        var result = await session.ListMessagesAsync("Gone", 0, 50, false, cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ImapSession.FolderNotFound, result.Error);
    }

    [Fact]
    public async Task GetMessageAsync_MapsAVanishedFolderToTheFolderSentinel()
    {
        using var server = new NoSuchFolderImapServer();
        server.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = await ConnectedSessionAsync(server, cts.Token);

        var result = await session.GetMessageAsync("Gone", 1, cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ImapSession.FolderNotFound, result.Error);
    }

    [Fact]
    public async Task GetAttachmentAsync_MapsAVanishedFolderToTheFolderSentinel()
    {
        using var server = new NoSuchFolderImapServer();
        server.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = await ConnectedSessionAsync(server, cts.Token);

        var result = await session.GetAttachmentAsync("Gone", 1, "1", cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ImapSession.FolderNotFound, result.Error);
    }
}

/// <summary>
/// The minimum of RFC 3501 needed to take an ImapClient through Connect and Authenticate, with
/// LIST always answering zero matches — no mailbox ever exists, so GetFolderAsync throws the
/// real MailKit FolderNotFoundException whatever path is requested. Same shape as
/// ImapSessionListFoldersTests' FakeImapServer, keyed on the command verb only.
/// </summary>
internal sealed class NoSuchFolderImapServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private Task? _serverLoop;

    private const string Caps = "IMAP4rev1 NAMESPACE";

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

                    // No "* LIST" line at all: MailKit's GetFolderAsync sees zero matches for
                    // whatever path it asked for and throws FolderNotFoundException itself.
                    case "LIST":
                        await writer.WriteLineAsync($"{tag} OK LIST completed");
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
            // The test's own assertions are the source of truth; a torn-down connection is
            // not a failure this loop needs to surface.
        }
    }

    public void Dispose()
    {
        _listener.Stop();
    }
}
