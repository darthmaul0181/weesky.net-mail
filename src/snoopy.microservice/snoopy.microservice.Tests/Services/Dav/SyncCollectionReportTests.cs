using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services.Dav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Dav;

public sealed class SyncCollectionReportTests
{
    private const string CollectionHref = "/m/";

    private static readonly Guid Epoch = Guid.NewGuid();

    [Fact]
    public async Task MembersAndTombstones_AreMergedByRank()
    {
        var state = new SyncState(Epoch, 5, 0);
        var source = new FakeMemberSource(
            new Dictionary<string, ulong> { ["a"] = 2, ["c"] = 5 }, [new DavTombstone("b", 3)]);
        var body = Body(TokenAt(1));
        var context = NewContext();

        var window = await SyncCollectionReport.ReadWindowAsync(body.Root!, state, source, CancellationToken.None);
        var outcome = await SyncCollectionReport.WriteAsync(context.Response, body, CollectionHref, null,
            window, source, CancellationToken.None);

        var responses = ResponsesOf(context.Response);
        Assert.Equal(["/m/a", "/m/b", "/m/c"], responses.Select(HrefOf));
        Assert.Equal("HTTP/1.1 404 Not Found", responses[1].Element(DavXml.Status)!.Value);
        Assert.Equal("\"c\"", responses[2].Descendants(DavXml.Dav + "getetag").Single().Value);
        Assert.Equal(3, outcome.Responses);
        Assert.Equal(DavSyncToken.Token(state), outcome.TokenOut);
        Assert.Equal(outcome.TokenOut, TokenWritten(context.Response));
    }

    [Fact]
    public async Task TheWindow_ReadsTheTombstonesOfTheTokensRange_OnASequenceTokenAlone()
    {
        var state = new SyncState(Epoch, 9, 0);
        var source = new FakeMemberSource();

        var window = await SyncCollectionReport.ReadWindowAsync(Body(TokenAt(2)).Root!, state, source,
            CancellationToken.None);

        Assert.Equal(SyncTokenKind.Sequence, window.Token.Kind);
        Assert.Equal([(2UL, 9UL)], source.TombstoneCalls);
        Assert.Same(state, window.State);
    }

    [Fact]
    public async Task AnInitialSync_ReadsNoTombstone()
    {
        var source = new FakeMemberSource(
            new Dictionary<string, ulong> { ["a"] = 1 }, [new DavTombstone("gone", 1)]);
        var context = NewContext();
        var body = Body(string.Empty);

        var window = await SyncCollectionReport.ReadWindowAsync(body.Root!, new SyncState(Epoch, 1, 0),
            source, CancellationToken.None);
        await SyncCollectionReport.WriteAsync(context.Response, body, CollectionHref, null, window, source,
            CancellationToken.None);

        // The answer is authoritative on what the collection holds; absence is what to forget.
        Assert.Empty(source.TombstoneCalls);
        Assert.Equal(["/m/a"], ResponsesOf(context.Response).Select(HrefOf));
    }

    [Fact]
    public async Task AnInvalidToken_IsRefusedBeforeAnythingIsWritten()
    {
        var source = new FakeMemberSource(new Dictionary<string, ulong> { ["a"] = 1 });
        var context = NewContext();
        var body = Body("not-a-token-of-ours");

        var window = await SyncCollectionReport.ReadWindowAsync(body.Root!, new SyncState(Epoch, 1, 0),
            source, CancellationToken.None);
        var refused = await Assert.ThrowsAsync<DavPreconditionException>(() => SyncCollectionReport.WriteAsync(
            context.Response, body, CollectionHref, null, window, source, CancellationToken.None));

        Assert.Equal(SyncTokenKind.Invalid, window.Token.Kind);
        Assert.Empty(source.TombstoneCalls);
        Assert.Equal(DavXml.Dav + "valid-sync-token", refused.Condition);
        Assert.Equal(0, ((MemoryStream)context.Response.Body).Length);
    }

    [Fact]
    public async Task TheTruncation_CutsOnARankBoundary_AndTheTokenNamesTheLastCompleteRank()
    {
        var state = new SyncState(Epoch, 3, 0);
        var source = new FakeMemberSource(
            new Dictionary<string, ulong> { ["a"] = 1, ["b"] = 2, ["c"] = 2, ["d"] = 3 });
        var body = Body(string.Empty, limit: 2);
        var context = NewContext();

        var window = await SyncCollectionReport.ReadWindowAsync(body.Root!, state, source, CancellationToken.None);
        var outcome = await SyncCollectionReport.WriteAsync(context.Response, body, CollectionHref, null,
            window, source, CancellationToken.None);

        // Rank 2 holds two rows and would pass the bound: it is withheld WHOLE, and the token resumes
        // at rank 1 — cut inside a rank, the rest of it would be abandoned for ever.
        var responses = ResponsesOf(context.Response);
        Assert.Equal(["/m/a", CollectionHref], responses.Select(HrefOf));
        Assert.Equal("HTTP/1.1 507 Insufficient Storage", responses[1].Element(DavXml.Status)!.Value);
        Assert.Equal(DavSyncToken.Token(state with { Seq = 1 }), outcome.TokenOut);
        Assert.Equal(outcome.TokenOut, TokenWritten(context.Response));
    }

    [Fact]
    public async Task TheFirstRank_IsServedWholeEvenPastTheBound()
    {
        var source = new FakeMemberSource(
            new Dictionary<string, ulong> { ["a"] = 1, ["b"] = 1, ["c"] = 1 });
        var body = Body(string.Empty, limit: 2);
        var context = NewContext();

        var window = await SyncCollectionReport.ReadWindowAsync(body.Root!, new SyncState(Epoch, 1, 0),
            source, CancellationToken.None);
        await SyncCollectionReport.WriteAsync(context.Response, body, CollectionHref, null, window, source,
            CancellationToken.None);

        Assert.Equal(["/m/a", "/m/b", "/m/c"], ResponsesOf(context.Response).Select(HrefOf));
    }

    [Fact]
    public async Task TheToken_IsMintedFromTheCounter_NotFromTheLastMember()
    {
        var state = new SyncState(Epoch, 9, 0);
        var source = new FakeMemberSource(new Dictionary<string, ulong> { ["a"] = 2 });
        var body = Body(string.Empty);
        var context = NewContext();

        var window = await SyncCollectionReport.ReadWindowAsync(body.Root!, state, source, CancellationToken.None);
        var outcome = await SyncCollectionReport.WriteAsync(context.Response, body, CollectionHref, null,
            window, source, CancellationToken.None);

        // The counter read before the rows bounds the window; a write landing above it is served next round.
        Assert.EndsWith("/9", outcome.TokenOut);
    }

    [Fact]
    public async Task ABodyWithoutSyncLevelNorDepth_IsA400()
    {
        var source = new FakeMemberSource();
        var body = Body(string.Empty, syncLevel: null);
        var context = NewContext();

        var window = await SyncCollectionReport.ReadWindowAsync(body.Root!, new SyncState(Epoch, 1, 0),
            source, CancellationToken.None);

        await Assert.ThrowsAsync<DavBadRequestException>(() => SyncCollectionReport.WriteAsync(
            context.Response, body, CollectionHref, null, window, source, CancellationToken.None));
        Assert.Equal(0, ((MemoryStream)context.Response.Body).Length);
    }

    private static string TokenAt(ulong sequence) => DavSyncToken.Token(new SyncState(Epoch, sequence, 0));

    private static XDocument Body(string token, int? limit = null, string? syncLevel = "1")
    {
        var root = new XElement(DavXml.Dav + "sync-collection",
            new XElement(DavXml.Dav + "sync-token", token));
        if (syncLevel is not null) root.Add(new XElement(DavXml.Dav + "sync-level", syncLevel));
        if (limit is { } bound)
            root.Add(new XElement(DavXml.Dav + "limit", new XElement(DavXml.Dav + "nresults", bound)));
        root.Add(new XElement(DavXml.Prop, new XElement(DavXml.Dav + "getetag")));
        return new XDocument(root);
    }

    private static DefaultHttpContext NewContext() => new() { Response = { Body = new MemoryStream() } };

    private static XDocument DocumentOf(HttpResponse response) =>
        XDocument.Parse(Encoding.UTF8.GetString(((MemoryStream)response.Body).ToArray()));

    private static List<XElement> ResponsesOf(HttpResponse response) =>
        [.. DocumentOf(response).Root!.Elements(DavXml.Response)];

    private static string TokenWritten(HttpResponse response) =>
        DocumentOf(response).Root!.Element(DavXml.Dav + "sync-token")!.Value;

    private static string HrefOf(XElement response) => response.Element(DavXml.Href)!.Value;
}
