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
/// af9df0c stopped ListFoldersAsync from resolving special uses itself: SpecialUse must
/// stay null so FolderRoleResolver's chain is the only thing that can decide a role, while
/// AttributeRole keeps carrying the server's raw flag as the chain's input. Nothing in the
/// existing suite drives ListFoldersAsync itself to pin that down — the mocked-session test
/// in MailFolderRepositoryTests only checks that a SpecialUse it set by hand round-trips
/// through the mock, and the controller test exercises StampRoles, not this method.
///
/// ImapSession takes a concrete MailKit ImapClient rather than an interface, so the only
/// way to exercise the real method is a real client talking to a real (if disposable)
/// server. FakeImapServer below is a loopback TCP listener that scripts just enough of an
/// IMAP session — greeting, LOGIN, NAMESPACE, LIST, STATUS — for GetFoldersAsync to
/// succeed, matching on the command keyword only so it does not depend on the exact bytes
/// a given MailKit version happens to send.
/// </summary>
public sealed class ImapSessionListFoldersTests
{
    [Fact]
    public async Task ListFoldersAsync_LeavesSpecialUseNullButKeepsAttributeRole()
    {
        using var server = new FakeImapServer();
        server.Start();

        using var client = new ImapClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await client.ConnectAsync("127.0.0.1", server.Port, SecureSocketOptions.None, cts.Token);
        await client.AuthenticateAsync("alice", "hunter2", cts.Token);

        await using var session = new ImapSession(client, Mock.Of<IMailHtmlSanitizer>(), Mock.Of<ILogger>());

        var result = await session.ListFoldersAsync(cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);

        // The server flagged Sent \Sent: AttributeRole is the chain's raw input and must
        // still carry it, but SpecialUse is the chain's *output* — this layer has no
        // business pre-deciding it, override or no override.
        var sent = Assert.Single(result.Value, n => n.Path == "Sent");
        Assert.Equal("sent", sent.AttributeRole);
        Assert.Null(sent.SpecialUse);

        // INBOX's role comes from the FullName check, not a server flag, but the same rule
        // applies: it is AttributeRole's job to carry it, never SpecialUse's.
        var inbox = Assert.Single(result.Value, n => n.Path == "INBOX");
        Assert.Equal("inbox", inbox.AttributeRole);
        Assert.Null(inbox.SpecialUse);
    }

    // The poll's change signal. UIDNEXT rises on every arrival; HIGHESTMODSEQ on every flag
    // change, which no counter sees — but only a CONDSTORE server may be asked for it.
    [Fact]
    public async Task ListFoldersAsync_CarriesTheChangeCountersWhenCondStoreIsAdvertised()
    {
        using var server = new FakeImapServer(condStore: true);
        server.Start();

        using var client = new ImapClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync("127.0.0.1", server.Port, SecureSocketOptions.None, cts.Token);
        await client.AuthenticateAsync("alice", "hunter2", cts.Token);
        await using var session = new ImapSession(client, Mock.Of<IMailHtmlSanitizer>(), Mock.Of<ILogger>());

        var result = await session.ListFoldersAsync(cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var inbox = Assert.Single(result.Value, n => n.Path == "INBOX");
        Assert.Equal(4u, inbox.UidNext);
        Assert.Equal(42ul, inbox.HighestModSeq);

        Assert.Contains(server.StatusRequests, request =>
            request.Contains("HIGHESTMODSEQ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListFoldersAsync_LeavesHighestModSeqNullWithoutCondStore()
    {
        using var server = new FakeImapServer();
        server.Start();

        using var client = new ImapClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync("127.0.0.1", server.Port, SecureSocketOptions.None, cts.Token);
        await client.AuthenticateAsync("alice", "hunter2", cts.Token);
        await using var session = new ImapSession(client, Mock.Of<IMailHtmlSanitizer>(), Mock.Of<ILogger>());

        var result = await session.ListFoldersAsync(cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var inbox = Assert.Single(result.Value, n => n.Path == "INBOX");
        Assert.Equal(4u, inbox.UidNext);
        Assert.Null(inbox.HighestModSeq);

        // The null alone proves nothing — the mapping also nulls it. The wire must show the
        // item was never REQUESTED: that is the protocol rule the capability gate exists for.
        Assert.NotEmpty(server.StatusRequests);
        Assert.All(server.StatusRequests, request =>
            Assert.DoesNotContain("HIGHESTMODSEQ", request, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// The minimum of RFC 3501 needed to take an ImapClient through Connect, Authenticate and
/// GetFoldersAsync: a greeting with capabilities inline, LOGIN, NAMESPACE, LIST and STATUS.
/// Responses are keyed on the command verb only, not the full command line, so the fake
/// does not need to track exactly which parameters a given MailKit version chooses to send.
/// </summary>
internal sealed class FakeImapServer : IDisposable
{
    private readonly bool _condStore;
    private readonly bool _oauth;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private Task? _serverLoop;

    public FakeImapServer(bool condStore = false, bool oauth = false)
    {
        _condStore = condStore;
        _oauth = oauth;
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Raw STATUS command lines, so tests can assert what was actually requested.</summary>
    public List<string> StatusRequests { get; } = new();

    /// <summary>The SASL mechanism the client actually chose.</summary>
    public string? AuthenticateMechanism { get; private set; }

    private string Caps
    {
        get
        {
            var baseCaps = _condStore
                ? "IMAP4rev1 NAMESPACE SPECIAL-USE CONDSTORE"
                : "IMAP4rev1 NAMESPACE SPECIAL-USE";
            return _oauth ? $"{baseCaps} AUTH=XOAUTH2 LOGINDISABLED" : baseCaps;
        }
    }

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

                    case "LIST":
                        // INBOX carries no server flag — its role comes from the FullName
                        // check. Sent carries \Sent, the ordinary server-declared case.
                        await writer.WriteLineAsync("* LIST (\\HasNoChildren) \"/\" \"INBOX\"");
                        await writer.WriteLineAsync("* LIST (\\HasNoChildren \\Sent) \"/\" \"Sent\"");
                        await writer.WriteLineAsync($"{tag} OK LIST completed");
                        break;

                    case "STATUS":
                        StatusRequests.Add(line);
                        var mailbox = ExtractMailboxName(parts.Length > 2 ? parts[2] : string.Empty);
                        var items = _condStore
                            ? "MESSAGES 3 UNSEEN 1 UIDVALIDITY 100 UIDNEXT 4 HIGHESTMODSEQ 42"
                            : "MESSAGES 3 UNSEEN 1 UIDVALIDITY 100 UIDNEXT 4";
                        await writer.WriteLineAsync($"* STATUS {mailbox} ({items})");
                        await writer.WriteLineAsync($"{tag} OK STATUS completed");
                        break;

                    case "LOGOUT":
                        await writer.WriteLineAsync("* BYE logging out");
                        await writer.WriteLineAsync($"{tag} OK LOGOUT completed");
                        return;

                    case "AUTHENTICATE":
                        AuthenticateMechanism = parts.Length > 2
                            ? parts[2].Split(' ')[0].ToUpperInvariant()
                            : string.Empty;
                        // The client sends the payload with the command (SASL-IR); nothing here
                        // reads it, since what is under test is the mechanism, not the secret.
                        await writer.WriteLineAsync($"{tag} OK [CAPABILITY {Caps}] AUTHENTICATE completed");
                        break;

                    default:
                        await writer.WriteLineAsync($"{tag} BAD unhandled command in fake server: {command}");
                        break;
                }
            }
        }
        catch (Exception)
        {
            // The test's own assertions are the source of truth; a torn-down connection
            // (test disposed the server, or the client disconnected first) is not a failure
            // this loop needs to surface.
        }
    }

    private static string ExtractMailboxName(string remainder)
    {
        var end = remainder.IndexOf(" (", StringComparison.Ordinal);
        return (end >= 0 ? remainder[..end] : remainder).Trim();
    }

    public void Dispose()
    {
        _listener.Stop();
    }
}
