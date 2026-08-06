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
/// Emptying a folder is documented as 1:* + \Deleted + a bare EXPUNGE, but the purge branch
/// used to SEARCH ALL first and send every UID back as an explicit set — a multi-hundred-KB
/// command line on a 100k-message trash, enumerating what the 1:* range already names. Only
/// the purge branch changes: a move still needs the UID list for MOVE.
/// </summary>
public sealed class ImapSessionEmptyFolderTests
{
    [Fact]
    public async Task EmptyAsync_PurgesWithTheWholeRangeAndNeverEnumeratesUids()
    {
        using var server = new EmptyFolderImapServer();
        server.Start();

        using var client = new ImapClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync("127.0.0.1", server.Port, SecureSocketOptions.None, cts.Token);
        await client.AuthenticateAsync("alice", "hunter2", cts.Token);
        await using var session = new ImapSession(client, Mock.Of<IMailHtmlSanitizer>(), Mock.Of<ILogger>());

        var result = await session.EmptyAsync("Trash", targetPath: null, cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);

        Assert.DoesNotContain(server.Commands, c => c.Contains(" SEARCH", StringComparison.OrdinalIgnoreCase));

        var store = Assert.Single(server.Commands, c => c.Contains("STORE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("1:*", store, StringComparison.Ordinal);
        Assert.Contains(@"\Deleted", store, StringComparison.OrdinalIgnoreCase);

        // A bare EXPUNGE, not UID EXPUNGE: purging the whole folder needs no UIDPLUS.
        var expunge = Assert.Single(server.Commands, c => c.Contains("EXPUNGE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("UID EXPUNGE", expunge, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Scripts enough of an IMAP session for EmptyAsync's purge branch — greeting, LOGIN,
/// NAMESPACE, LIST, SELECT, STORE, EXPUNGE (and SEARCH, answering matches, so the test proves
/// it is never asked rather than that it would fail) — recording every command line.
/// </summary>
internal sealed class EmptyFolderImapServer : IDisposable
{
    private const string Caps = "IMAP4rev1 NAMESPACE";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private Task? _serverLoop;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public List<string> Commands { get; } = new();

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
                var remainder = parts.Length > 2 ? parts[2] : string.Empty;

                if (command == "UID")
                {
                    var space = remainder.IndexOf(' ');
                    command = (space < 0 ? remainder : remainder[..space]).ToUpperInvariant();
                }

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
                        await writer.WriteLineAsync("* LIST (\\HasNoChildren \\Trash) \"/\" \"Trash\"");
                        await writer.WriteLineAsync($"{tag} OK LIST completed");
                        break;

                    case "SELECT":
                    case "EXAMINE":
                        await writer.WriteLineAsync("* 3 EXISTS");
                        await writer.WriteLineAsync("* 0 RECENT");
                        await writer.WriteLineAsync("* FLAGS (\\Answered \\Flagged \\Deleted \\Seen \\Draft)");
                        await writer.WriteLineAsync(@"* OK [PERMANENTFLAGS (\Deleted \Seen \*)] Flags permitted");
                        await writer.WriteLineAsync("* OK [UIDVALIDITY 100] UIDs valid");
                        await writer.WriteLineAsync("* OK [UIDNEXT 4] Predicted next UID");
                        await writer.WriteLineAsync($"{tag} OK [READ-WRITE] SELECT completed");
                        break;

                    case "SEARCH":
                        Commands.Add(line);
                        await writer.WriteLineAsync("* SEARCH 1 2 3");
                        await writer.WriteLineAsync($"{tag} OK SEARCH completed");
                        break;

                    case "STORE":
                        Commands.Add(line);
                        await writer.WriteLineAsync($"{tag} OK STORE completed");
                        break;

                    case "EXPUNGE":
                        Commands.Add(line);
                        await writer.WriteLineAsync($"{tag} OK EXPUNGE completed");
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
            // The test's own assertions are the source of truth; a torn-down connection is not a
            // failure this loop needs to surface.
        }
    }

    public void Dispose() => _listener.Stop();
}
