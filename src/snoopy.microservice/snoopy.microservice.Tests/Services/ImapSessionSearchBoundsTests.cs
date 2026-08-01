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
/// The merge-path search (all folders, or a server without SORT) used to FETCH the envelope of
/// every SEARCH match before paging — unbounded work for one page of results, and the reachable
/// <c>{ hasAttachment: true }</c> request compiled to SEARCH ALL, sweeping the whole mailbox.
/// The fix bounds the per-folder candidate FETCH to <c>(page + 1) * pageSize</c>, the only
/// window that can still reach the requested page once the folders are merged. SEARCH itself
/// stays unbounded (a UID list is cheap) so Total stays exact whenever no post-filter runs.
/// Same seam as the sibling loopback tests: ImapSession wraps a concrete ImapClient.
/// </summary>
public sealed class ImapSessionSearchBoundsTests
{
    private static MailSearchCriteria Criteria(bool hasAttachment) =>
        new(null, null, null, null, null, null, false, false, hasAttachment);

    private static async Task<ImapSession> ConnectedSessionAsync(SearchBoundsImapServer server, CancellationToken token)
    {
        var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", server.Port, SecureSocketOptions.None, token);
        await client.AuthenticateAsync("alice", "hunter2", token);
        return new ImapSession(client, Mock.Of<IMailHtmlSanitizer>(), Mock.Of<ILogger>());
    }

    [Fact]
    public async Task SearchAsync_FetchesOnlyThePageWindowOfCandidatesOnTheMergePath()
    {
        // No SORT capability forces the merge path even on a single folder — the path that used
        // to fetch the envelope of every SEARCH match before paging.
        using var server = new SearchBoundsImapServer(sort: false, searchUids: Enumerable.Range(1, 10));
        server.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = await ConnectedSessionAsync(server, cts.Token);

        var result = await session.SearchAsync("INBOX", allFolders: false, Criteria(hasAttachment: false), 0, 2, cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);

        // (page + 1) * pageSize = 2 candidates, taken UID-descending as the newest-first proxy.
        Assert.Equal(new[] { 9u, 10u }, server.FetchedUidSets[0].OrderBy(uid => uid));

        Assert.Equal(10, result.Value.Total);
        Assert.Equal(2, result.Value.Results.Count);
    }

    [Fact]
    public async Task SearchAsync_KeepsTotalExactWithoutTheAttachmentFilter()
    {
        using var server = new SearchBoundsImapServer(sort: false, searchUids: Enumerable.Range(1, 5));
        server.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = await ConnectedSessionAsync(server, cts.Token);

        var result = await session.SearchAsync("INBOX", allFolders: false, Criteria(hasAttachment: false), 3, 2, cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);

        // The page lies past the last match, but SEARCH already said how many match: Total is
        // exact, paid for with a UID list rather than a per-message fetch.
        Assert.Equal(5, result.Value.Total);
        Assert.Empty(result.Value.Results);
    }

    [Fact]
    public async Task SearchAsync_PrefersServerSortOverTheUidProxyOnTheMergePath()
    {
        using var server = new SearchBoundsImapServer(sort: true, searchUids: Enumerable.Range(1, 10));
        server.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = await ConnectedSessionAsync(server, cts.Token);

        var result = await session.SearchAsync("INBOX", allFolders: true, Criteria(hasAttachment: false), 0, 2, cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);

        // Rule 2: when the session advertises SORT the server hands the order — the UID proxy
        // is only the fallback. The candidate window stays bounded either way.
        var sortCommand = Assert.Single(server.SortCommands);
        Assert.Contains("REVERSE DATE", sortCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(server.SearchCommands);

        Assert.Equal(new[] { 9u, 10u }, server.FetchedUidSets[0].OrderBy(uid => uid));
        Assert.Equal(10, result.Value.Total);
    }

    [Fact]
    public async Task SearchAsync_FindsAnAttachmentPastThePageWindowWithinTheScanBudget()
    {
        // The realistic case the page-window bound over-corrected: 500 matches, the only
        // attachment-bearing one 300 deep. The scan budget must reach it — 500 BODYSTRUCTUREs
        // is a cheap scan, not the whole-mailbox sweep the bound exists to stop.
        using var server = new SearchBoundsImapServer(
            sort: false, searchUids: Enumerable.Range(1, 500), attachmentUids: [200]);
        server.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = await ConnectedSessionAsync(server, cts.Token);

        var result = await session.SearchAsync("INBOX", allFolders: false, Criteria(hasAttachment: true), 0, 2, cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(500, server.FetchedUidSets[0].Count);
        Assert.Equal(1, result.Value.Total);
        var row = Assert.Single(result.Value.Results);
        Assert.Equal(200u, row.Uid);
        Assert.True(row.HasAttachments);
    }

    [Fact]
    public async Task SearchAsync_CapsTheAttachmentScanAtItsBudget()
    {
        using var server = new SearchBoundsImapServer(sort: false, searchUids: Enumerable.Range(1, 2500));
        server.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = await ConnectedSessionAsync(server, cts.Token);

        var result = await session.SearchAsync("INBOX", allFolders: false, Criteria(hasAttachment: true), 0, 2, cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);

        // The pathological sweep is still cut: exactly the budget of newest candidates, no more.
        var fetched = Assert.Single(server.FetchedUidSets);
        Assert.Equal(ImapSession.AttachmentScanBudget, fetched.Count);
        Assert.Equal(501u, fetched.Min());
        Assert.Equal(2500u, fetched.Max());
    }

    [Fact]
    public async Task SearchAsync_AppliesTheScanBudgetOnTheSingleFolderSortPath()
    {
        // Same over-correction existed on the SORT branch: its attachment filter must scan to
        // the budget too, not stop at the page window.
        using var server = new SearchBoundsImapServer(
            sort: true, searchUids: Enumerable.Range(1, 500), attachmentUids: [200]);
        server.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = await ConnectedSessionAsync(server, cts.Token);

        var result = await session.SearchAsync("INBOX", allFolders: false, Criteria(hasAttachment: true), 0, 2, cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Single(server.SortCommands);
        Assert.Equal(1, result.Value.Total);
        var row = Assert.Single(result.Value.Results);
        Assert.Equal(200u, row.Uid);
    }
}

/// <summary>
/// Scripts enough of an IMAP session for SearchAsync: greeting, LOGIN, NAMESPACE, LIST,
/// EXAMINE, UID SEARCH / UID SORT (answering a configured UID list, newest last / first
/// respectively) and UID FETCH — per requested UID, envelope + INTERNALDATE + a BODYSTRUCTURE
/// that carries an attachment only for the configured UIDs, plus the partial text-part fetch
/// MailKit issues for previews — recording each so tests can assert what was asked for.
/// </summary>
internal sealed class SearchBoundsImapServer : IDisposable
{
    private readonly bool _sort;
    private readonly uint[] _searchUids;
    private readonly HashSet<uint> _attachmentUids;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private Task? _serverLoop;

    private const string Envelope =
        "(\"Sat, 01 Aug 2026 10:00:00 +0000\" \"Hi\" ((\"Alice\" NIL \"alice\" \"weesky.be\")) " +
        "((\"Alice\" NIL \"alice\" \"weesky.be\")) ((\"Alice\" NIL \"alice\" \"weesky.be\")) " +
        "((\"Bob\" NIL \"bob\" \"weesky.be\")) NIL NIL NIL \"<m@weesky.be>\")";

    private const string TextOnlyBodyStructure =
        "(\"text\" \"plain\" (\"charset\" \"utf-8\") NIL NIL \"7bit\" 5 1 NIL NIL NIL NIL)";

    private const string AttachmentBodyStructure =
        "((\"text\" \"plain\" (\"charset\" \"utf-8\") NIL NIL \"7bit\" 5 1 NIL NIL NIL NIL)" +
        "(\"application\" \"pdf\" (\"name\" \"a.pdf\") NIL NIL \"base64\" 12 NIL " +
        "(\"attachment\" (\"filename\" \"a.pdf\")) NIL NIL) " +
        "\"mixed\" (\"boundary\" \"b\") NIL NIL NIL)";

    public SearchBoundsImapServer(bool sort, IEnumerable<int> searchUids, IEnumerable<int>? attachmentUids = null)
    {
        _sort = sort;
        _searchUids = searchUids.Select(uid => (uint)uid).ToArray();
        _attachmentUids = (attachmentUids ?? []).Select(uid => (uint)uid).ToHashSet();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public List<string> SearchCommands { get; } = new();
    public List<string> SortCommands { get; } = new();
    public List<string> FetchCommands { get; } = new();

    /// <summary>The UID set of each UID FETCH, expanded, so bounding is asserted on the wire.</summary>
    public List<IReadOnlyList<uint>> FetchedUidSets { get; } = new();

    private string Caps => _sort ? "IMAP4rev1 NAMESPACE SORT" : "IMAP4rev1 NAMESPACE";

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

                // UID SEARCH/SORT/FETCH arrive as "<tag> UID <verb> ..."; key on the verb.
                if (command == "UID")
                {
                    var space = remainder.IndexOf(' ');
                    command = (space < 0 ? remainder : remainder[..space]).ToUpperInvariant();
                    remainder = space < 0 ? string.Empty : remainder[(space + 1)..];
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
                        await writer.WriteLineAsync("* LIST (\\HasNoChildren) \"/\" \"INBOX\"");
                        await writer.WriteLineAsync($"{tag} OK LIST completed");
                        break;

                    case "SELECT":
                    case "EXAMINE":
                        await writer.WriteLineAsync($"* {_searchUids.Length} EXISTS");
                        await writer.WriteLineAsync("* 0 RECENT");
                        await writer.WriteLineAsync("* FLAGS (\\Answered \\Flagged \\Deleted \\Seen \\Draft)");
                        await writer.WriteLineAsync("* OK [PERMANENTFLAGS ()] Read-only");
                        await writer.WriteLineAsync("* OK [UIDVALIDITY 100] UIDs valid");
                        await writer.WriteLineAsync($"* OK [UIDNEXT {_searchUids.Length + 1}] Predicted next UID");
                        await writer.WriteLineAsync($"{tag} OK [READ-ONLY] EXAMINE completed");
                        break;

                    case "SEARCH":
                        SearchCommands.Add(line);
                        await writer.WriteLineAsync($"* SEARCH {string.Join(' ', _searchUids)}");
                        await writer.WriteLineAsync($"{tag} OK SEARCH completed");
                        break;

                    case "SORT":
                        SortCommands.Add(line);
                        await writer.WriteLineAsync($"* SORT {string.Join(' ', _searchUids.Reverse())}");
                        await writer.WriteLineAsync($"{tag} OK SORT completed");
                        break;

                    case "FETCH":
                        FetchCommands.Add(line);
                        var uids = ExpandSet(remainder.Split(' ', 2)[0]);
                        FetchedUidSets.Add(uids);

                        // MailKit computes previews itself on a server without PREVIEW, with a
                        // partial fetch of the text part; answer it — echoing the section it
                        // asked for — or the page fetch never completes.
                        if (line.Contains("]<0.", StringComparison.Ordinal))
                        {
                            var open = line.IndexOf("BODY.PEEK[", StringComparison.OrdinalIgnoreCase) + 10;
                            var section = line[open..line.IndexOf(']', open)];
                            foreach (var uid in uids)
                                await writer.WriteLineAsync($"* {uid} FETCH (UID {uid} BODY[{section}]<0> {{5}}\r\nhello)");
                            await writer.WriteLineAsync($"{tag} OK FETCH completed");
                            break;
                        }

                        foreach (var uid in uids)
                        {
                            var structure = _attachmentUids.Contains(uid) ? AttachmentBodyStructure : TextOnlyBodyStructure;
                            await writer.WriteLineAsync(
                                $"* {uid} FETCH (UID {uid} INTERNALDATE \"01-Aug-2026 10:00:00 +0000\" " +
                                $"ENVELOPE {Envelope} BODYSTRUCTURE {structure})");
                        }
                        await writer.WriteLineAsync($"{tag} OK FETCH completed");
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

    /// <summary>Expands an IMAP UID set ("9:10", "1,3:5") into plain UIDs, either range order.</summary>
    private static List<uint> ExpandSet(string set)
    {
        var uids = new List<uint>();
        foreach (var token in set.Split(','))
        {
            var range = token.Split(':');
            var first = uint.Parse(range[0]);
            var last = range.Length > 1 ? uint.Parse(range[1]) : first;
            var (low, high) = first <= last ? (first, last) : (last, first);
            for (var uid = low; uid <= high; uid++) uids.Add(uid);
        }

        return uids;
    }

    public void Dispose() => _listener.Stop();
}
