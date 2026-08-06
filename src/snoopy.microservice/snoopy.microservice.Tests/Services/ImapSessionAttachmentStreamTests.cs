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
/// Downloading an attachment must fetch the part's bytes once — BODY.PEEK[n] by specifier —
/// not through GetBodyPart, whose extra [n.MIME] sub-fetch and MimeParser pass buy nothing the
/// BODYSTRUCTURE in hand does not already say. The decoded copy must stay seekable (the
/// frontend relies on Content-Length and download progress) and correctly transfer-decoded.
/// </summary>
public sealed class ImapSessionAttachmentStreamTests
{
    [Fact]
    public async Task GetAttachmentAsync_StreamsThePartBySpecifierAndDecodesIt()
    {
        using var server = new AttachmentImapServer();
        server.Start();

        using var client = new ImapClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync("127.0.0.1", server.Port, SecureSocketOptions.None, cts.Token);
        await client.AuthenticateAsync("alice", "hunter2", cts.Token);
        await using var session = new ImapSession(client, Mock.Of<IMailHtmlSanitizer>(), Mock.Of<ILogger>());

        var result = await session.GetAttachmentAsync("INBOX", 1, "2", cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal("big.pdf", result.Value.FileName);
        Assert.Equal("application/pdf", result.Value.ContentType);

        // Seekable and decoded: base64 "JVBERi0xLjQK" is the PDF magic plus a newline.
        Assert.True(result.Value.Content.CanSeek);
        using var reader = new MemoryStream();
        await result.Value.Content.CopyToAsync(reader, cts.Token);
        Assert.Equal("%PDF-1.4\n"u8.ToArray(), reader.ToArray());

        Assert.DoesNotContain(server.FetchCommands,
            c => c.Contains("2.MIME", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Scripts enough of an IMAP session for GetAttachmentAsync: greeting, LOGIN, NAMESPACE, LIST,
/// EXAMINE, a BODYSTRUCTURE fetch, and the part fetches — both the reduced BODY.PEEK[2] the
/// fix issues and GetBodyPart's [2.MIME]+[2] pair, so the pre-fix path completes and the test
/// fails on the command it pins rather than on an unrelated protocol error.
/// </summary>
internal sealed class AttachmentImapServer : IDisposable
{
    private const string Caps = "IMAP4rev1 NAMESPACE";

    private const string Base64Payload = "JVBERi0xLjQK";

    private const string BodyStructure =
        "((\"text\" \"html\" (\"charset\" \"utf-8\") NIL NIL \"7bit\" 28 1 NIL NIL NIL NIL)" +
        "(\"application\" \"pdf\" (\"name\" \"big.pdf\") NIL NIL \"base64\" 12 NIL " +
        "(\"attachment\" (\"filename\" \"big.pdf\")) NIL NIL) " +
        "\"mixed\" (\"boundary\" \"bnd\") NIL NIL NIL)";

    private static readonly string PartMimeHeaders = string.Join("\r\n",
        "Content-Type: application/pdf; name=\"big.pdf\"",
        "Content-Disposition: attachment; filename=\"big.pdf\"",
        "Content-Transfer-Encoding: base64",
        "",
        "");

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private Task? _serverLoop;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public List<string> FetchCommands { get; } = new();

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

                if (command == "UID" && remainder.StartsWith("FETCH", StringComparison.OrdinalIgnoreCase))
                    command = "FETCH";

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
                        await writer.WriteLineAsync("* FLAGS (\\Answered \\Flagged \\Deleted \\Seen \\Draft)");
                        await writer.WriteLineAsync("* OK [PERMANENTFLAGS ()] Read-only");
                        await writer.WriteLineAsync("* OK [UIDVALIDITY 100] UIDs valid");
                        await writer.WriteLineAsync("* OK [UIDNEXT 2] Predicted next UID");
                        await writer.WriteLineAsync($"{tag} OK [READ-ONLY] EXAMINE completed");
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
            // The test's own assertions are the source of truth; a torn-down connection is not a
            // failure this loop needs to surface.
        }
    }

    private static async Task RespondToFetchAsync(StreamWriter writer, string tag, string line)
    {
        if (line.Contains("2.MIME", StringComparison.OrdinalIgnoreCase))
        {
            await writer.WriteAsync($"* 1 FETCH (UID 1 BODY[2.MIME] {{{PartMimeHeaders.Length}}}\r\n{PartMimeHeaders}");
            await writer.WriteLineAsync($" BODY[2] {{{Base64Payload.Length}}}\r\n{Base64Payload})");
            await writer.WriteLineAsync($"{tag} OK FETCH completed");
            return;
        }

        if (line.Contains("BODY.PEEK[2]", StringComparison.OrdinalIgnoreCase))
        {
            await writer.WriteLineAsync($"* 1 FETCH (UID 1 BODY[2] {{{Base64Payload.Length}}}\r\n{Base64Payload})");
            await writer.WriteLineAsync($"{tag} OK FETCH completed");
            return;
        }

        await writer.WriteLineAsync($"* 1 FETCH (UID 1 BODYSTRUCTURE {BodyStructure})");
        await writer.WriteLineAsync($"{tag} OK FETCH completed");
    }

    public void Dispose() => _listener.Stop();
}
