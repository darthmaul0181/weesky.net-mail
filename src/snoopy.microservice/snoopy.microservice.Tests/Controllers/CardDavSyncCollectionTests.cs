using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class CardDavSyncCollectionTests : IAsyncLifetime
{
    private static readonly Guid Epoch = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private DavTestServer server = null!;
    private ulong counter;
    private bool counterPinned;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync();
        // The state row exists with a KNOWN epoch before any request: without it the report would
        // mint a fresh epoch and every TokenAt(n) below would be refused as foreign.
        UpsertState(seq: 0);
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task AnInitialSync_AnswersTheWholeBookAndNoTombstone()
    {
        GivenCards("a.vcf", "b.vcf");
        GivenATombstone("gone.vcf", rank: 3);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(token: null));

        // An empty token means the whole book and no tombstones: the book is authoritative on what
        // it holds, and cards absent from the initial answer are what the client must forget.
        Assert.Equal(2, ResponsesOfStatus(response, 200).Count);
        Assert.Empty(ResponsesOfStatus(response, 404));
    }

    [Fact]
    public async Task AnIncrementalSync_AnswersOnlyWhatMovedSince()
    {
        GivenCardAtRank("a.vcf", 5);
        GivenCardAtRank("b.vcf", 12);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(8)));

        Assert.Single(HrefsOf(response), h => h.EndsWith("b.vcf"));
        Assert.DoesNotContain(HrefsOf(response), h => h.EndsWith("a.vcf"));
    }

    [Fact]
    public async Task ACardAtExactlyTheTokenRank_IsNotReServed()
    {
        GivenCardAtRank("seen.vcf", 8);
        GivenCardAtRank("fresh.vcf", 12);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(8)));

        // The window opens STRICTLY above the token: rank 8 is what the token says the client
        // already holds, and a ">=" would re-serve every card of the very rank it stored.
        Assert.Single(HrefsOf(response), h => h.EndsWith("fresh.vcf"));
        Assert.DoesNotContain(HrefsOf(response), h => h.EndsWith("seen.vcf"));
    }

    [Fact]
    public async Task ATombstoneInTheWindow_ComesBackAs404()
    {
        GivenATombstone("gone.vcf", rank: 12);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(8)));

        var gone = ResponsesOfStatus(response, 404).Single();
        // A direct child of its response, never lodged in a propstat.
        Assert.Single(gone.Elements(DavXml.Dav + "status"));
        Assert.Empty(gone.Elements(DavXml.Dav + "propstat"));
    }

    [Fact]
    public async Task ATombstoneAtExactlyTheTokenRank_IsNotReServed()
    {
        GivenATombstone("old.vcf", rank: 8);
        GivenATombstone("gone.vcf", rank: 12);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(8)));

        var gone = ResponsesOfStatus(response, 404).Single();
        Assert.EndsWith("gone.vcf", gone.Element(DavXml.Href)!.Value);
    }

    [Fact]
    public async Task ATombstonePastTheCounter_IsNotServedUnderTheReturnedToken()
    {
        GivenTheCounterAt(20);
        GivenATombstone("late.vcf", rank: 25);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(8)));

        // The same <= seq bound as the cards: a deletion past the counter belongs to the next
        // round, where the token it travels under actually covers it.
        Assert.Empty(ResponsesOfStatus(response, 404));
    }

    [Fact]
    public async Task ItServesThePropertiesTheRequestAsked_AndNotAHardCodedEtag()
    {
        GivenCardAtRank("a.vcf", 12);

        var response = await Report(DavPaths.Collection(UserId),
            SyncBody(TokenAt(8), props: ["getetag", "resourcetype"]));

        // DAVx5 asks for getetag AND resourcetype, and uses the second to rule out sub-collections.
        var body = await response.ReadAsync();
        Assert.Contains("getetag", body);
        Assert.Contains("resourcetype", body);
    }

    [Fact]
    public async Task AddressDataInASyncCollection_ComesBackAs404_AndThatIsAChoice()
    {
        GivenCardAtRank("a.vcf", 12);

        var response = await Report(DavPaths.Collection(UserId),
            SyncBody(TokenAt(8), carddavProps: ["address-data"]));

        // RFC 6352 § 10.4 defines address-data only in query and multiget. Serving it here would put
        // on the sync report the weight decision 15 spares it — a batch of five hundred 1 MB cards.
        // Thunderbird tries it and chains a multiget when the property is missing from the propstat.
        var propstat404 = XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.Dav + "propstat")
            .Single(p => p.Element(DavXml.Dav + "status")!.Value.Contains("404"));
        Assert.Single(propstat404.Descendants(DavXml.CardDav + "address-data"));
    }

    [Fact]
    public async Task AResponseAskedForNoProperty_StillCarriesAStatusOfItsOwn()
    {
        GivenCardAtRank("a.vcf", 12);
        GivenATombstone("gone.vcf", 13);

        // The tester's own body (vreports/sync/2.xml): an EMPTY prop, which asks for nothing at all.
        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(8), props: []));

        // RFC 4918 § 14.24: a response is (href, status) or (href, propstat+). With neither propstat
        // written, an href alone is a document no conforming client can read.
        var card = ResponseOf(response, "a.vcf");
        Assert.Equal("HTTP/1.1 200 OK", card.Elements(DavXml.Dav + "status").Single().Value);
        Assert.Empty(card.Elements(DavXml.Dav + "propstat"));

        // And the tombstone keeps the 404 it has always carried: the fallback fires on emptiness,
        // never on a response that already said something.
        Assert.Equal("HTTP/1.1 404 Not Found",
            ResponseOf(response, "gone.vcf").Elements(DavXml.Dav + "status").Single().Value);
    }

    [Fact]
    public async Task TheNewTokenIsTheCounterReadBeforeTheRows()
    {
        GivenCardAtRank("a.vcf", 12);
        GivenTheCounterAt(20);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(8)));

        // Reading the rows first and the counter after would let a write committed in between be
        // COVERED by the returned token without appearing in the answer: the client would believe it
        // seen, never ask again, and the card would be missing for ever — no error, no trace.
        Assert.Equal(DavSyncToken.Token(new SyncState(Epoch, 20, 0)), NewTokenOf(response));
    }

    [Fact]
    public async Task ACounterAdvancedWhileTheRowsAreRead_DoesNotReachTheReturnedToken()
    {
        // InMemory has no transactions, so no snapshot can pin the read ORDER — this reader can:
        // it advances the counter the moment the rows are enumerated. A report reading rows first
        // and the counter second would return the advanced value and cover a write it never served.
        await using var racing = await DavTestServer.StartAsync(
            overrides: services => services.AddScoped<IDavContactReader, CounterBumpingReader>());
        using (var db = racing.CreateContext())
        {
            db.ContactSyncStates.Add(new ContactSyncState
            {
                UserId = racing.UserId, Epoch = Epoch, Seq = 20, PrunedBelow = 0
            });
            SeedCard(db, racing.UserId, "a.vcf", 12);
            db.SaveChanges();
        }

        var response = await racing.SendAsync(
            "REPORT", DavPaths.Collection(racing.UserId), BuildSyncBody(TokenAt(8)));

        Assert.Equal(207, response.StatusCode);
        Assert.Single(HrefsOf(response), h => h.EndsWith("a.vcf"));
        Assert.Equal(DavSyncToken.Token(new SyncState(Epoch, 20, 0)), NewTokenOf(response));
    }

    [Fact]
    public async Task ARowWrittenAfterTheCounterWasRead_IsNotServedUnderTheReturnedToken()
    {
        GivenTheCounterAt(20);
        GivenCardAtRank("late.vcf", 25);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(8)));

        // The `<= seq` upper bound is what makes the claim true even when the rows are not read in
        // the same transaction as the counter. At worst the client gets it next round.
        Assert.DoesNotContain(HrefsOf(response), h => h.EndsWith("late.vcf"));
    }

    [Fact]
    public async Task ARefusedToken_Answers403ValidSyncToken()
    {
        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(2), pruned: 10));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "valid-sync-token", ConditionOf(response));
    }

    [Fact]
    public async Task ATruncatedInitialSyncOnAPrunedBook_EmitsATokenTheServerAcceptsBack()
    {
        GivenCardAtRank("a.vcf", 10);
        GivenCardAtRank("b.vcf", 11);
        GivenTheCounterAt(60);
        GivenPrunedBelow(50);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(token: null, limit: 1));

        // No rank of this window sits above the watermark, so no legal cut exists: the whole
        // window is served instead. Cutting at rank 10 would emit a token Read itself refuses,
        // and RFC 6578 sect. 3.2 then loops the client on the same truncated initial sync for ever.
        Assert.Equal(2, ResponsesOfStatus(response, 200).Count);
        Assert.Empty(ResponsesOfStatus(response, 507));
        Assert.Equal(SyncTokenKind.Sequence,
            ReadBack(NewTokenOf(response), new SyncState(Epoch, 60, 50)));
    }

    [Fact]
    public async Task ATruncatedCut_LandsOnTheFirstRankWhoseTokenReadsBack()
    {
        GivenCardAtRank("a.vcf", 10);
        GivenCardAtRank("b.vcf", 50);
        GivenCardAtRank("c.vcf", 55);
        GivenCardAtRank("d.vcf", 56);
        GivenTheCounterAt(60);
        GivenPrunedBelow(50);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(token: null, limit: 1));

        // Rank 10 cannot host the cut - its token is strictly below the watermark and Read
        // refuses it - so it is served through. Rank 50 can: a token AT the watermark reads back
        // (ruling BG), every tombstone above it having survived the prune.
        Assert.Equal(2, ResponsesOfStatus(response, 200).Count);
        Assert.Single(ResponsesOfStatus(response, 507));
        Assert.Equal(DavSyncToken.Token(new SyncState(Epoch, 50, 0)), NewTokenOf(response));
    }

    [Theory]
    [InlineData(true, null, 50ul)]
    [InlineData(true, 1, 50ul)]
    [InlineData(false, 1, 50ul)]
    [InlineData(true, null, 60ul)]
    [InlineData(true, 1, 60ul)]
    [InlineData(false, null, 60ul)]
    public async Task EveryEmittedToken_IsOneTheServerAcceptsBack(
        bool initial, int? limit, ulong watermark)
    {
        GivenCardAtRank("a.vcf", 10);
        GivenCardAtRank("b.vcf", 50);
        GivenCardAtRank("c.vcf", 55);
        GivenCardAtRank("d.vcf", 56);
        GivenATombstone("gone.vcf", 58);
        GivenTheCounterAt(60);
        GivenPrunedBelow(watermark);

        var response = await Report(DavPaths.Collection(UserId),
            SyncBody(initial ? null : TokenAt(watermark), limit: limit));

        // The invariant itself, not one case of it: whatever the server emits - initial or
        // incremental, truncated or whole - fed straight back to Read against the same state must
        // never be Invalid. A refused emission is the announced-then-refused loop in disguise.
        // Watermark 60 is the sealed book (Seq == pruned_below, the newest event a pruned
        // deletion) - the very state that exposed the old "<=" refusal; the incremental cases
        // also pin end to end that a token AT the watermark is served, not answered 403.
        Assert.Equal(207, response.StatusCode);
        Assert.Equal(SyncTokenKind.Sequence,
            ReadBack(NewTokenOf(response), new SyncState(Epoch, 60, watermark)));
    }

    [Fact]
    public async Task TheSyncTokenAPropfindEmits_IsOneTheReportAcceptsBack()
    {
        GivenCardAtRank("a.vcf", 10);
        GivenTheCounterAt(60);
        GivenPrunedBelow(60);

        var response = await server.PropfindAsync(DavPaths.Collection(UserId), "0",
            new XDocument(new XElement(DavXml.Dav + "propfind",
                new XElement(DavXml.Prop, new XElement(DavXml.Dav + "sync-token")))).ToString());

        // The first token a client ever reads comes from PROPFIND, not from the report. On the
        // sealed book the old "<=" refusal made the next REPORT refuse this very value - the
        // pairing dead on arrival, ctag poll and 403 in a loop.
        Assert.Equal(207, response.StatusCode);
        var emitted = XDocument.Parse(response.Body)
            .Descendants(DavXml.Dav + "sync-token").Single().Value;
        Assert.Equal(SyncTokenKind.Sequence, ReadBack(emitted, new SyncState(Epoch, 60, 60)));
    }

    [Fact]
    public async Task ALimit_TruncatesOnARankBoundaryAndSaysSo()
    {
        GivenCardsAtRank(rank: 10, count: 3);
        GivenCardsAtRank(rank: 11, count: 3);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(0), limit: 4));

        // The cut cannot fall in the middle of a rank: a batch carries several rows at one sequence,
        // and returning token n after serving only part of rank n would abandon the rest for ever.
        // Whole ranks while the count stays under the bound, then the token of the last COMPLETE one.
        Assert.Equal(3, ResponsesOfStatus(response, 200).Count);
        Assert.Equal(DavSyncToken.Token(new SyncState(Epoch, 10, 0)), NewTokenOf(response));
        Assert.Single(ResponsesOfStatus(response, 507));
    }

    [Fact]
    public async Task ATruncatedAnswer_ServesTheCompleteRankUnderItsOwnHrefs()
    {
        var first = GivenCardsAtRank(rank: 10, count: 3);
        GivenCardsAtRank(rank: 11, count: 3);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(0), limit: 4));

        // A count of three cannot see three responses under the WRONG hrefs — rank 11's, or a
        // mix of both ranks. The names are random on purpose, underivable from the ranks.
        Assert.Equal(
            first.Select(name => DavPaths.Card(UserId, name)).Order(),
            ResponsesOfStatus(response, 200).Select(r => r.Element(DavXml.Href)!.Value).Order());
    }

    [Fact]
    public async Task ASingleRankBiggerThanTheLimit_IsServedWhole()
    {
        GivenCardsAtRank(rank: 10, count: 5);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(0), limit: 2));

        // Exceeding the requested bound is an inconvenience; losing half of a rank is data loss.
        Assert.Equal(5, ResponsesOfStatus(response, 200).Count);
    }

    [Fact]
    public async Task TheLimitIsReadInTheDavNamespaceAndNotTheCarddavOne()
    {
        GivenCardsAtRank(rank: 10, count: 3);

        var response = await Report(DavPaths.Collection(UserId),
            SyncBody(TokenAt(0), carddavLimit: 1));

        // Both exist and share a local name: RFC 6578 § 3.6 defines this one in DAV:, RFC 6352 § 10.6
        // defines addressbook-query's in the carddav namespace. A CARDDAV:limit here is not ours.
        Assert.Equal(3, ResponsesOfStatus(response, 200).Count);
    }

    [Fact]
    public async Task ACarddavLimitSpanningTwoRanks_DoesNotTruncateEither()
    {
        GivenCardsAtRank(rank: 10, count: 2);
        GivenCardsAtRank(rank: 11, count: 2);

        var response = await Report(DavPaths.Collection(UserId),
            SyncBody(TokenAt(0), carddavLimit: 2));

        // The single-rank fixture above cannot see an honoured CARDDAV:limit — a first rank is
        // served whole under any bound. Two ranks is where honouring it would actually truncate.
        Assert.Equal(4, ResponsesOfStatus(response, 200).Count);
        Assert.Empty(ResponsesOfStatus(response, 507));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("infinite")]
    public async Task AValidSyncLevel_IsAccepted(string level) =>
        Assert.Equal(207, (await Report(DavPaths.Collection(UserId),
            SyncBody(TokenAt(0), syncLevel: level))).StatusCode);

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("infinity")]
    public async Task AnAbsentSyncLevel_FallsBackOnAnyDepthHeader(string depth)
    {
        var response = await Report(DavPaths.Collection(UserId),
            SyncBody(TokenAt(0), syncLevel: null), depth: depth);

        // Appendix A's fallback, read wider than the letter on purpose: taken literally, § 3's
        // "Depth: 0" plus appendix A refuses with 400 the client that set the CONFORMING header and
        // forgot the one element the RFC introduced to replace it — punishing the closest to the norm
        // on its very first request.
        Assert.Equal(207, response.StatusCode);
    }

    [Fact]
    public async Task AnAbsentSyncLevelAndNoDepthAtAll_Answers400()
    {
        var response = await Report(DavPaths.Collection(UserId),
            SyncBody(TokenAt(0), syncLevel: null), depth: null);

        // Nothing left to convert.
        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task ASyncLevelOfAnotherValue_Answers400()
    {
        var response = await Report(DavPaths.Collection(UserId),
            SyncBody(TokenAt(0), syncLevel: "2"));

        // Accepting would be guessing what the client meant, on the report where one can least
        // afford it.
        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task ADepthHeaderOtherThanZero_IsIgnoredRatherThanRefused()
    {
        var response = await Report(DavPaths.Collection(UserId),
            SyncBody(TokenAt(0), syncLevel: "1"), depth: "1");

        // § 3 says literally that any other value gives a 400. We do not, and sabre does not either:
        // refusing a Depth: 1 a client set out of habit buys nothing but a book that will not pair.
        // Named divergence for 4d, not a discovery.
        Assert.Equal(207, response.StatusCode);
    }

    private Task<DavTestResponse> Report(string path, string? body, string? depth = null) =>
        server.SendAsync("REPORT", path, body, depth);

    private static string TokenAt(ulong sequence) =>
        DavSyncToken.Token(new SyncState(Epoch, sequence, 0));

    private void GivenCards(params string[] names)
    {
        foreach (var name in names) GivenCardAtRank(name, counter + 1);
    }

    private void GivenCardAtRank(string davName, ulong rank)
    {
        using var db = server.CreateContext();
        SeedCard(db, UserId, davName, rank);
        db.SaveChanges();
        RaiseCounterTo(rank);
    }

    private List<string> GivenCardsAtRank(ulong rank, int count)
    {
        // Random names on purpose: names derivable from the rank would leave the href assertions
        // unable to tell one rank's cards from another's.
        List<string> names = [.. Enumerable.Range(0, count).Select(_ => $"{Guid.NewGuid():N}.vcf")];
        foreach (var name in names) GivenCardAtRank(name, rank);
        return names;
    }

    private void GivenATombstone(string davName, ulong rank)
    {
        using var db = server.CreateContext();
        db.ContactTombstones.Add(new ContactTombstone
        {
            UserId = UserId, DavName = davName, SyncSequence = rank, DeletedAt = DateTime.UtcNow
        });
        db.SaveChanges();
        RaiseCounterTo(rank);
    }

    private void GivenTheCounterAt(ulong seq)
    {
        counterPinned = true;
        counter = seq;
        UpsertState(seq);
    }

    private void GivenPrunedBelow(ulong watermark) => UpsertState(counter, watermark);

    private static SyncTokenKind ReadBack(string token, SyncState state) =>
        DavSyncToken.Read(new XElement(DavXml.Dav + "sync-token", token), state).Kind;

    /// <summary>Follows the highest seeded rank, unless a test pinned the counter itself.</summary>
    private void RaiseCounterTo(ulong rank)
    {
        if (counterPinned || rank <= counter) return;
        counter = rank;
        UpsertState(rank);
    }

    private void UpsertState(ulong seq, ulong? prunedBelow = null)
    {
        using var db = server.CreateContext();
        var row = db.ContactSyncStates.SingleOrDefault(s => s.UserId == UserId);
        if (row is null)
        {
            db.ContactSyncStates.Add(new ContactSyncState
            {
                UserId = UserId, Epoch = Epoch, Seq = seq, PrunedBelow = prunedBelow ?? 0
            });
        }
        else
        {
            row.Seq = seq;
            if (prunedBelow is { } watermark) row.PrunedBelow = watermark;
        }

        db.SaveChanges();
    }

    private static void SeedCard(PreferencesTestDbContext db, Guid owner, string davName, ulong rank)
    {
        var id = Guid.NewGuid();
        db.Contacts.Add(new Contact
        {
            Id = id,
            UserId = owner,
            Uid = id.ToString(),
            DavName = davName,
            VCardRaw = $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u-{davName}\r\nFN:{davName}\r\nEND:VCARD\r\n",
            CardHash = $"hash-of-{davName}",
            UpdatedAt = DateTime.UtcNow,
            SyncSequence = rank,
        });
    }

    /// <summary>Raises the pruning watermark before building the body carrying the doomed token.</summary>
    private string SyncBody(string? token, string[]? props = null, string[]? carddavProps = null,
        int? limit = null, int? carddavLimit = null, string? syncLevel = "1", ulong? pruned = null)
    {
        if (pruned is { } watermark) UpsertState(Math.Max(counter, watermark), watermark);
        return BuildSyncBody(token, props, carddavProps, limit, carddavLimit, syncLevel);
    }

    private static string BuildSyncBody(string? token, string[]? props = null,
        string[]? carddavProps = null, int? limit = null, int? carddavLimit = null,
        string? syncLevel = "1")
    {
        var root = new XElement(DavXml.Dav + "sync-collection",
            new XElement(DavXml.Dav + "sync-token", token ?? string.Empty));
        if (syncLevel is not null) root.Add(new XElement(DavXml.Dav + "sync-level", syncLevel));
        if (limit is { } bound)
            root.Add(new XElement(DavXml.Dav + "limit", new XElement(DavXml.Dav + "nresults", bound)));
        if (carddavLimit is { } foreignBound)
            root.Add(new XElement(DavXml.CardDav + "limit",
                new XElement(DavXml.CardDav + "nresults", foreignBound)));

        var prop = new XElement(DavXml.Prop);
        foreach (var name in props ?? ["getetag"]) prop.Add(new XElement(DavXml.Dav + name));
        foreach (var name in carddavProps ?? []) prop.Add(new XElement(DavXml.CardDav + name));
        root.Add(prop);

        return new XDocument(root).ToString();
    }

    private static XName ConditionOf(DavTestResponse response) =>
        XDocument.Parse(response.Body).Root!.Elements().Single().Name;

    private static string NewTokenOf(DavTestResponse response) =>
        XDocument.Parse(response.Body).Root!.Element(DavXml.Dav + "sync-token")!.Value;

    private static List<XElement> ResponsesOf(DavTestResponse response) =>
        [.. XDocument.Parse(response.Body).Root!.Elements(DavXml.Response)];

    private static XElement ResponseOf(DavTestResponse response, string davName) =>
        ResponsesOf(response).Single(r => r.Element(DavXml.Href)!.Value.EndsWith(davName, StringComparison.Ordinal));

    private static List<string> HrefsOf(DavTestResponse response) =>
        [.. ResponsesOf(response).Select(r => r.Element(DavXml.Href)!.Value)];

    private static List<XElement> ResponsesOfStatus(DavTestResponse response, int statusCode) =>
        [.. ResponsesOf(response)
            .Where(r => r.Descendants(DavXml.Status).Any(s => s.Value.Contains($" {statusCode} ")))];

    /// <summary>
    /// Real reads over the server's own database, but the first enumeration of the changed rows
    /// advances the counter first — the concurrent write the retained read order tolerates.
    /// </summary>
    private sealed class CounterBumpingReader(PreferencesDbContext context) : IDavContactReader
    {
        private readonly DavContactReader inner = new(context);

        public async IAsyncEnumerable<DavCard> ChangedAsync(Guid userId, ulong after, ulong upTo,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var state = await context.ContactSyncStates.SingleAsync(
                s => s.UserId == userId, cancellationToken);
            state.Seq = 99;
            await context.SaveChangesAsync(cancellationToken);

            await foreach (var card in inner.ChangedAsync(userId, after, upTo, cancellationToken))
                yield return card;
        }

        public Task<IReadOnlyList<ContactTombstone>> TombstonesAsync(
            Guid userId, ulong after, ulong upTo, CancellationToken cancellationToken) =>
            inner.TombstonesAsync(userId, after, upTo, cancellationToken);

        public IAsyncEnumerable<DavCard> StreamAsync(
            Guid userId, ulong upTo, CancellationToken cancellationToken) =>
            inner.StreamAsync(userId, upTo, cancellationToken);

        public Task<DavCard?> FindAsync(Guid userId, string davName, CancellationToken cancellationToken) =>
            inner.FindAsync(userId, davName, cancellationToken);

        public Task<IReadOnlyList<DavCard>> FindManyAsync(
            Guid userId, IReadOnlyList<string> davNames, CancellationToken cancellationToken) =>
            inner.FindManyAsync(userId, davNames, cancellationToken);

        public Task<int> CountAsync(Guid userId, CancellationToken cancellationToken) =>
            inner.CountAsync(userId, cancellationToken);
    }
}
