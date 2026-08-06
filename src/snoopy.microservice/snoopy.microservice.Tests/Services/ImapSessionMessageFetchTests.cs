using System.Net;
using System.Net.Sockets;
using System.Text;
using CSharpFunctionalExtensions;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// Opening a message must not download its attachments. <c>GetMessageAsync</c> already FETCHes
/// BODYSTRUCTURE — which is what the attachment list is built from — so the parts it still needs
/// are the text bodies and the headers, both addressable on their own. Asking for the whole
/// message instead pulls every attachment over IMAP to render a few kilobytes of body, on every
/// open, and pays it again when the user actually clicks the attachment.
///
/// The assertion is on the command issued rather than on bytes transferred: the defect is that
/// <c>BODY.PEEK[]</c> (empty section = the entire RFC822 message) is asked for at all, and a fake
/// server that genuinely returned 27 MB would make the suite slow to prove something a one-line
/// assertion states exactly.
/// </summary>
public sealed class ImapSessionMessageFetchTests
{
    [Fact]
    public async Task GetMessageAsync_DoesNotDownloadTheWholeMessage()
    {
        using var server = new MessageFetchImapServer();
        server.Start();

        using var client = new ImapClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await client.ConnectAsync("127.0.0.1", server.Port, SecureSocketOptions.None, cts.Token);
        await client.AuthenticateAsync("alice", "hunter2", cts.Token);

        var sanitizer = new Mock<IMailHtmlSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
                 .Returns((string html) => new SanitizedHtml { Html = html });

        await using var session = new ImapSession(client, sanitizer.Object, Mock.Of<ILogger>());

        var result = await session.GetMessageAsync("INBOX", 1, cts.Token);

        // The body and the attachment listing must still be complete — a cheaper fetch that lost
        // either of them would be a regression, not a fix.
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Contains("Hello", result.Value.HtmlBody, StringComparison.Ordinal);
        var attachment = Assert.Single(result.Value.Attachments);
        Assert.Equal("big.pdf", attachment.FileName);

        Assert.True(
            !server.FetchCommands.Exists(c => c.Contains("BODY.PEEK[]", StringComparison.OrdinalIgnoreCase)
                                              || c.Contains("RFC822", StringComparison.OrdinalIgnoreCase)),
            "Opening a message asked for the whole RFC822 payload. Commands issued:\n  "
            + string.Join("\n  ", server.FetchCommands));
    }

    private static async Task<Result<MailMessageDetail>> GetSinglePartMessageAsync(
        SinglePartImapServer server, CancellationToken token)
    {
        using var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", server.Port, SecureSocketOptions.None, token);
        await client.AuthenticateAsync("alice", "hunter2", token);

        var sanitizer = new Mock<IMailHtmlSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
                 .Returns((string html) => new SanitizedHtml { Html = html });

        await using var session = new ImapSession(client, sanitizer.Object, Mock.Of<ILogger>());
        return await session.GetMessageAsync("INBOX", 1, token);
    }

    // MimeMessage.TextBody unflows RFC 3676 format=flowed — the default of Thunderbird, Apple
    // Mail and most lists — before returning; the reduced fetch must too, or every plain-text
    // mail renders hard-wrapped at ~72 columns under the reader's pre-wrap styling.
    [Fact]
    public async Task GetMessageAsync_UnflowsAFormatFlowedTextBody()
    {
        const string structure =
            "(\"text\" \"plain\" (\"charset\" \"utf-8\" \"format\" \"flowed\") NIL NIL \"7bit\" 64 3 NIL NIL NIL NIL)";
        const string body = "This is a paragraph that was \r\nwrapped by the composer.\r\n";
        using var server = new SinglePartImapServer(structure, body);
        server.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var result = await GetSinglePartMessageAsync(server, cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Contains("that was wrapped by the composer.", result.Value.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMessageAsync_HonoursDelSpWhenUnflowing()
    {
        const string structure =
            "(\"text\" \"plain\" (\"charset\" \"utf-8\" \"format\" \"flowed\" \"delsp\" \"yes\") NIL NIL \"7bit\" 16 2 NIL NIL NIL NIL)";
        // delsp=yes: the soft break's trailing space is part of the break and must vanish.
        const string body = "unwr \r\napped.\r\n";
        using var server = new SinglePartImapServer(structure, body);
        server.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var result = await GetSinglePartMessageAsync(server, cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Contains("unwrapped.", result.Value.TextBody, StringComparison.Ordinal);
    }

    // A server may answer NIL for body_fld_enc; that null must read as the default encoding,
    // not turn opening the message into a 502.
    [Fact]
    public async Task GetMessageAsync_SurvivesANilTransferEncoding()
    {
        const string structure =
            "(\"text\" \"plain\" (\"charset\" \"utf-8\") NIL NIL NIL 12 1 NIL NIL NIL NIL)";
        using var server = new SinglePartImapServer(structure, "Plain body.\r\n");
        server.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var result = await GetSinglePartMessageAsync(server, cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Contains("Plain body.", result.Value.TextBody, StringComparison.Ordinal);
    }

    // MimeKit refuses an attachment-disposed part as the message body; MailKit's non-multipart
    // TextBody/HtmlBody branch does not, so the guard is ours. A sole text/plain filed as an
    // attachment belongs on the attachment list, not in the reader pane.
    [Fact]
    public async Task GetMessageAsync_DoesNotRenderAnAttachmentDisposedSolePartAsTheBody()
    {
        const string structure =
            "(\"text\" \"plain\" (\"charset\" \"utf-8\") NIL NIL \"7bit\" 6 1 NIL " +
            "(\"attachment\" (\"filename\" \"notes.txt\")) NIL NIL)";
        using var server = new SinglePartImapServer(structure, "Notes.");
        server.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var result = await GetSinglePartMessageAsync(server, cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(string.Empty, result.Value.TextBody);
        Assert.Equal(string.Empty, result.Value.HtmlBody);
        var attachment = Assert.Single(result.Value.Attachments);
        Assert.Equal("notes.txt", attachment.FileName);
    }

    [Fact]
    public async Task GetAttachmentAsync_SurvivesANilTransferEncoding()
    {
        const string structure =
            "(\"application\" \"octet-stream\" (\"name\" \"raw.bin\") NIL NIL NIL 4 NIL " +
            "(\"attachment\" (\"filename\" \"raw.bin\")) NIL NIL)";
        using var server = new SinglePartImapServer(structure, "data");
        server.Start();

        using var client = new ImapClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync("127.0.0.1", server.Port, SecureSocketOptions.None, cts.Token);
        await client.AuthenticateAsync("alice", "hunter2", cts.Token);
        await using var session = new ImapSession(client, Mock.Of<IMailHtmlSanitizer>(), Mock.Of<ILogger>());

        var result = await session.GetAttachmentAsync("INBOX", 1, "", cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        using var reader = new MemoryStream();
        await result.Value.Content.CopyToAsync(reader, cts.Token);
        Assert.Equal("data"u8.ToArray(), reader.ToArray());
    }
}

/// <summary>
/// Scripts just enough of an IMAP session for <c>GetMessageAsync</c> to succeed — greeting,
/// LOGIN, NAMESPACE, LIST, EXAMINE, and the two FETCH shapes it uses — while recording every
/// FETCH line so a test can assert what was asked for. Matching is on the command keyword only,
/// so it does not depend on the exact bytes a given MailKit version sends.
///
/// The PDF part is declared at 27 MB in BODYSTRUCTURE but never materialised: what the test
/// measures is the request, and serving the real thing would only make the suite slow.
/// </summary>
internal sealed class MessageFetchImapServer : IDisposable
{
    private const string Caps = "IMAP4rev1 NAMESPACE";

    private const string BodyStructure =
        "((\"TEXT\" \"HTML\" (\"CHARSET\" \"utf-8\") NIL NIL \"7BIT\" 28 1 NIL NIL NIL NIL)" +
        "(\"APPLICATION\" \"PDF\" (\"NAME\" \"big.pdf\") NIL NIL \"BASE64\" 27000000 NIL " +
        "(\"ATTACHMENT\" (\"FILENAME\" \"big.pdf\")) NIL NIL) " +
        "\"MIXED\" (\"BOUNDARY\" \"bnd\") NIL NIL NIL)";

    private const string Envelope =
        "(\"Sat, 01 Aug 2026 10:00:00 +0000\" \"Big attachment\" " +
        "((\"Alice\" NIL \"alice\" \"weesky.be\")) ((\"Alice\" NIL \"alice\" \"weesky.be\")) " +
        "((\"Alice\" NIL \"alice\" \"weesky.be\")) ((\"Bob\" NIL \"bob\" \"weesky.be\")) " +
        "NIL NIL NIL \"<msg1@weesky.be>\")";

    private static readonly string Message = string.Join("\r\n",
        "From: Alice <alice@weesky.be>",
        "To: Bob <bob@weesky.be>",
        "Subject: Big attachment",
        "Date: Sat, 01 Aug 2026 10:00:00 +0000",
        "Message-Id: <msg1@weesky.be>",
        "MIME-Version: 1.0",
        "Content-Type: multipart/mixed; boundary=\"bnd\"",
        "",
        "--bnd",
        "Content-Type: text/html; charset=utf-8",
        "Content-Transfer-Encoding: 7bit",
        "",
        "<html><body>Hello</body></html>",
        "--bnd",
        "Content-Type: application/pdf; name=\"big.pdf\"",
        "Content-Disposition: attachment; filename=\"big.pdf\"",
        "Content-Transfer-Encoding: base64",
        "",
        "JVBERi0xLjQK",
        "--bnd--",
        "");

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private Task? _serverLoop;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Raw FETCH command lines, so a test can assert what was actually requested.</summary>
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

                // UID FETCH arrives as "<tag> UID FETCH ..."; the keyword that matters is the second word.
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
        if (line.Contains("BODY.PEEK[]", StringComparison.OrdinalIgnoreCase)
            || line.Contains("RFC822", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = Encoding.ASCII.GetByteCount(Message);
            await writer.WriteLineAsync($"* 1 FETCH (UID 1 BODY[] {{{bytes}}}");
            await writer.WriteAsync(Message);
            await writer.WriteLineAsync(")");
            await writer.WriteLineAsync($"{tag} OK FETCH completed");
            return;
        }

        // The reduced fetch the fix should issue: one text part, addressed by its specifier.
        if (line.Contains("BODY.PEEK[1]", StringComparison.OrdinalIgnoreCase))
        {
            const string html = "<html><body>Hello</body></html>";
            await writer.WriteLineAsync($"* 1 FETCH (UID 1 BODY[1] {{{html.Length}}}");
            await writer.WriteAsync(html);
            await writer.WriteLineAsync(")");
            await writer.WriteLineAsync($"{tag} OK FETCH completed");
            return;
        }

        if (line.Contains("HEADER", StringComparison.OrdinalIgnoreCase))
        {
            var headers = Message[..(Message.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)];
            await writer.WriteLineAsync($"* 1 FETCH (UID 1 BODY[HEADER] {{{headers.Length}}}");
            await writer.WriteAsync(headers);
            await writer.WriteLineAsync(")");
            await writer.WriteLineAsync($"{tag} OK FETCH completed");
            return;
        }

        await writer.WriteLineAsync($"* 1 FETCH (UID 1 ENVELOPE {Envelope} BODYSTRUCTURE {BodyStructure})");
        await writer.WriteLineAsync($"{tag} OK FETCH completed");
    }

    public void Dispose() => _listener.Stop();
}

/// <summary>
/// Same session script as <see cref="MessageFetchImapServer"/> but for a single-part message
/// whose BODYSTRUCTURE and body text the test supplies — the shapes the reduced fetch must
/// decode faithfully (format=flowed, NIL encodings, attachment-disposed sole parts). The body
/// fetch echoes whatever section was asked for, so the non-multipart TEXT section works too.
/// </summary>
internal sealed class SinglePartImapServer : IDisposable
{
    private const string Caps = "IMAP4rev1 NAMESPACE";

    private const string Envelope =
        "(\"Sat, 01 Aug 2026 10:00:00 +0000\" \"Plain\" ((\"Alice\" NIL \"alice\" \"weesky.be\")) " +
        "((\"Alice\" NIL \"alice\" \"weesky.be\")) ((\"Alice\" NIL \"alice\" \"weesky.be\")) " +
        "((\"Bob\" NIL \"bob\" \"weesky.be\")) NIL NIL NIL \"<p@weesky.be>\")";

    private const string Headers =
        "From: Alice <alice@weesky.be>\r\nTo: Bob <bob@weesky.be>\r\nSubject: Plain\r\n" +
        "Date: Sat, 01 Aug 2026 10:00:00 +0000\r\nMessage-Id: <p@weesky.be>\r\n\r\n";

    private readonly string _bodyStructure;
    private readonly string _bodyText;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private Task? _serverLoop;

    public SinglePartImapServer(string bodyStructure, string bodyText)
    {
        _bodyStructure = bodyStructure;
        _bodyText = bodyText;
    }

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

    private async Task RespondToFetchAsync(StreamWriter writer, string tag, string line)
    {
        if (line.Contains("HEADER", StringComparison.OrdinalIgnoreCase))
        {
            await writer.WriteLineAsync($"* 1 FETCH (UID 1 BODY[HEADER] {{{Headers.Length}}}\r\n{Headers})");
            await writer.WriteLineAsync($"{tag} OK FETCH completed");
            return;
        }

        if (line.Contains("BODY.PEEK[", StringComparison.OrdinalIgnoreCase))
        {
            var open = line.IndexOf("BODY.PEEK[", StringComparison.OrdinalIgnoreCase) + 10;
            var section = line[open..line.IndexOf(']', open)];
            var bytes = Encoding.ASCII.GetByteCount(_bodyText);
            await writer.WriteLineAsync($"* 1 FETCH (UID 1 BODY[{section}] {{{bytes}}}\r\n{_bodyText})");
            await writer.WriteLineAsync($"{tag} OK FETCH completed");
            return;
        }

        await writer.WriteLineAsync($"* 1 FETCH (UID 1 ENVELOPE {Envelope} BODYSTRUCTURE {_bodyStructure})");
        await writer.WriteLineAsync($"{tag} OK FETCH completed");
    }

    public void Dispose() => _listener.Stop();
}
