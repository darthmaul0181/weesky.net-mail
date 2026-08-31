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

public sealed class CardDavPropfindTests : IAsyncLifetime
{
    private static readonly Guid Epoch = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private DavTestServer server = null!;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync() => server = await DavTestServer.StartAsync();

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task AnAbsentDepth_IsRefusedRatherThanGuessed()
    {
        var response = await Propfind(DavPaths.Collection(UserId), depth: null, body: null);

        // sabre guesses 1, Radicale guesses 0 — two different answers to the same silence. And
        // guessing 0 would give a VALID multistatus carrying only the collection, which a client
        // asking for 1 reads as an empty book — and an empty book it applies by erasing its copies.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "propfind-finite-depth", ConditionOf(response));
    }

    [Fact]
    public async Task DepthInfinity_IsRefusedTheSameWay()
    {
        var response = await Propfind(DavPaths.Collection(UserId), depth: "infinity", body: null);

        Assert.Equal(403, response.StatusCode);
    }

    [Fact]
    public async Task AnAbsentDepth_OffACollection_IsDepthZero()
    {
        // RFC 4918 § 9.1 reserves propfind-finite-depth to collections: on a card, the principal
        // and the service root, infinity IS depth 0, and sabre and Radicale both answer it.
        GivenCards("a.vcf");

        foreach (var path in new[] { DavPaths.Card(UserId, "a.vcf"), DavPaths.Principal(UserId), DavPaths.Root + "/" })
        {
            var response = await Propfind(path, depth: null, body: PropBody("resourcetype"));

            Assert.Equal(207, response.StatusCode);
            Assert.Single(ResponsesOf(response));
        }
    }

    [Fact]
    public async Task DepthZeroOnTheCollection_AnswersTheCollectionAlone()
    {
        // The card is what makes the assertion say anything: on an empty book a collection leaking
        // its members into a Depth: 0 answer is still a single response.
        GivenCards("a.vcf");

        var response = await Propfind(DavPaths.Collection(UserId), depth: "0", body: PropBody("displayname"));

        Assert.Equal(207, response.StatusCode);
        Assert.Single(ResponsesOf(response));
    }

    [Fact]
    public async Task DepthOneOnTheCollection_AnswersTheCollectionThenOneResponsePerCard()
    {
        // The second name needs escaping: two escape-invariant names would let a concatenated href
        // coincide with DavPaths.Card and leave the construction unpinned.
        GivenCards("a.vcf", "plan #9.vcf");

        var response = await Propfind(DavPaths.Collection(UserId), depth: "1", body: PropBody("getetag"));

        // The collection comes first with its trailing slash, then every member under the EXACT
        // href a client will GET after discovery — a member href built any other way 404s on every
        // cycle, so the construction itself is pinned, not merely the count and the shape.
        Assert.Equal(
            [DavPaths.Collection(UserId), DavPaths.Card(UserId, "a.vcf"), DavPaths.Card(UserId, "plan #9.vcf")],
            HrefsOf(response));
    }

    [Fact]
    public async Task DepthOne_LeavesOutTheCardsTheProtocolCannotSee()
    {
        GivenCards("a.vcf");
        GivenACardWithNoName();

        var response = await Propfind(DavPaths.Collection(UserId), depth: "1", body: PropBody("getetag"));

        // A book that serves a dead href is one a client flags in error on every cycle.
        Assert.Equal(2, HrefsOf(response).Count);
    }

    [Fact]
    public async Task DepthOne_BoundsItsMembersToTheCounterItRead()
    {
        GivenTheCounterAt(20);
        GivenCardAtRank("late.vcf", 25);
        GivenCardAtRank("a.vcf", 5);

        var response = await Propfind(DavPaths.Collection(UserId), depth: "1", body: PropBody("getetag"));

        // The forgotten path: DAVx5 reads the state then the member list in two separate PROPFINDs
        // and holds the ctag as covering the list. A row committed in between would be covered by
        // the returned ctag without appearing in the list.
        Assert.DoesNotContain(HrefsOf(response), h => h.EndsWith("late.vcf"));
        Assert.Contains(HrefsOf(response), h => h.EndsWith("a.vcf"));
    }

    [Fact]
    public async Task DepthOne_ReturnsTheSameCounterItBoundedWith()
    {
        GivenTheCounterAt(20);
        GivenCardAtRank("a.vcf", 5);

        var response = await Propfind(DavPaths.Collection(UserId), depth: "1",
            body: PropBody("getctag", DavXml.CalendarServer));

        // The two halves of the answer must be coherent with each other: the ctag returned is the
        // one the member list was bounded by, never a second read.
        Assert.Equal(DavSyncToken.Ctag(new SyncState(Epoch, 20, 0)), CtagOf(response));
    }

    [Fact]
    public async Task DepthZero_ReadsTheCounterOnceToo()
    {
        GivenTheCounterAt(20);
        GivenCardAtRank("a.vcf", 5);

        var response = await Propfind(DavPaths.Collection(UserId), depth: "0",
            body: PropBody("getctag", DavXml.CalendarServer));

        // The card again: without one, a book leaking its members into a Depth: 0 answer would
        // leave both of these assertions standing.
        Assert.Single(ResponsesOf(response));
        Assert.Equal(DavSyncToken.Ctag(new SyncState(Epoch, 20, 0)), CtagOf(response));
    }

    [Fact]
    public async Task DepthOne_ReadsTheCounterAndTheMembersInOneSnapshot()
    {
        // Outside one snapshot the bound is not free, and what it costs is a MODIFIED card: an
        // edit gives it a new, higher rank, so a webmail edit landing between the two reads moves
        // it above the bound and OUT of the member list, while the ctag still covers its old rank.
        // A client reads that absence as a server-side delete and drops its copy until the next
        // ctag poll — hours, by DAVx5's default.
        //
        // InMemory can never show that: it has no isolation, and ignoring TransactionIgnoredWarning
        // even makes CurrentTransaction stay null after BeginTransactionAsync, so a witness has
        // nothing to look at. Keeping the warning fatal turns "a snapshot was opened" into the one
        // observable there is — the refusal below is the provider's, never a route's answer.
        await using var strict = await DavTestServer.StartAsync(keepTransactionsFatal: true);
        using (var db = strict.CreateContext())
        {
            SeedCard(db, strict.UserId, "a.vcf", 5);
            db.SaveChanges();
        }

        var collection = DavPaths.Collection(strict.UserId);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => strict.PropfindAsync(collection, "1", PropBody("getetag")));

        // And only where both reads happen: a Depth: 0 reads the counter alone, one statement that
        // owes nothing to a snapshot, and it must not have paid for one.
        Assert.Equal(207, (await strict.PropfindAsync(collection, "0", PropBody("getetag"))).StatusCode);
    }

    [Fact]
    public async Task ASecondReadOfTheCounter_WouldContradictTheAnswerAlreadyGiven()
    {
        // Both orders answer the same thing on a quiet book, so the book is made to move: this
        // store answers a counter that has advanced to every read past the first. One read serves
        // the ctag AND the bound, so both assertions below hold; a second read feeds 99 to
        // whichever half asked for it — the ctag, or the bound that then lets late.vcf through.
        await using var drifting = await DavTestServer.StartAsync(
            overrides: services => services.AddScoped<IContactSyncStore, DriftingSyncStore>());
        using (var db = drifting.CreateContext())
        {
            db.ContactSyncStates.Add(new ContactSyncState
            {
                UserId = drifting.UserId, Epoch = Epoch, Seq = 20, PrunedBelow = 0
            });
            SeedCard(db, drifting.UserId, "a.vcf", 5);
            SeedCard(db, drifting.UserId, "late.vcf", 25);
            db.SaveChanges();
        }

        var response = await drifting.PropfindAsync(DavPaths.Collection(drifting.UserId), "1",
            PropBody([DavXml.CalendarServer + "getctag", DavXml.Dav + "getetag"]));

        Assert.Equal(DavSyncToken.Ctag(new SyncState(Epoch, 20, 0)),
            CtagOf(response, DavPaths.Collection(drifting.UserId)));
        Assert.DoesNotContain(HrefsOf(response), h => h.EndsWith("late.vcf"));
    }

    [Fact]
    public async Task ABookWithNoCounterAtAll_StillAnswersItsMembers()
    {
        // No state row means no rank was ever issued, so there is no claim to keep honest — and
        // bounding at 0 would answer an empty book, which a client applies by deleting its copies.
        GivenCardAtRank("a.vcf", 5);

        var response = await Propfind(DavPaths.Collection(UserId), depth: "1", body: PropBody("getetag"));

        Assert.Contains(HrefsOf(response), h => h.EndsWith("a.vcf"));
    }

    [Fact]
    public async Task DepthZeroOnTheHome_AnswersTheHomeAlone()
    {
        var response = await Propfind(DavPaths.Home(UserId), depth: "0", body: PropBody("displayname"));

        Assert.Single(ResponsesOf(response));
    }

    [Fact]
    public async Task DepthOneOnTheHome_AnswersTheHomeAndTheDefaultCollection()
    {
        var response = await Propfind(DavPaths.Home(UserId), depth: "1", body: PropBody("resourcetype"));

        var hrefs = HrefsOf(response);
        Assert.Equal([DavPaths.Home(UserId), DavPaths.Collection(UserId)], hrefs);
    }

    [Fact]
    public async Task ThePrincipal_AnswersItsHomeSet()
    {
        var response = await Propfind(DavPaths.Principal(UserId), depth: "0",
            body: PropBody("addressbook-home-set", DavXml.CardDav));

        var homeSet = XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.CardDav + "addressbook-home-set").Single();
        Assert.Equal(DavPaths.Home(UserId), homeSet.Element(DavXml.Dav + "href")!.Value);
    }

    [Fact]
    public async Task TheServiceRoot_AnswersCurrentUserPrincipal()
    {
        var response = await Propfind("/dav/", depth: "0", body: PropBody("current-user-principal"));

        var principal = XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.Dav + "current-user-principal").Single();
        Assert.Equal(DavPaths.Principal(UserId), principal.Element(DavXml.Dav + "href")!.Value);
    }

    [Fact]
    public async Task TheBareRoot_AnswersCurrentUserPrincipalToo()
    {
        // A client given the bare host tries the root as much as the well-known; two more lines
        // spare it failing on a path we do not use ourselves.
        var response = await Propfind("/", depth: "0", body: PropBody("current-user-principal"));

        Assert.Equal(207, response.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnIntermediateCollection_AnswersDepthZeroAsACollection(bool principals)
    {
        var path = principals ? DavPaths.PrincipalCollection : DavPaths.BookCollection;

        var response = await Propfind(path, depth: "0",
            body: PropBody("resourcetype", "current-user-principal"));

        // principal-collection-set PUBLISHES "/dav/principals/", and RFC 3744 § 5.8 makes it a URL
        // the client is entitled to walk: a 404 there says the server contradicts its own property.
        Assert.Equal(207, response.StatusCode);
        Assert.Equal([path], HrefsOf(response));
        var document = XDocument.Parse(await response.ReadAsync());
        Assert.Single(document.Descendants(DavXml.Dav + "resourcetype")
            .Single().Elements(DavXml.Dav + "collection"));
        Assert.Equal(DavPaths.Principal(UserId), document
            .Descendants(DavXml.Dav + "current-user-principal").Single()
            .Element(DavXml.Dav + "href")!.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DepthOneOnAnIntermediateCollection_ListsThisAccountsChildAndOnlyIt(bool principals)
    {
        var path = principals ? DavPaths.PrincipalCollection : DavPaths.BookCollection;
        var child = principals ? DavPaths.Principal(UserId) : DavPaths.Home(UserId);

        var response = await Propfind(path, depth: "1", body: PropBody("resourcetype"));

        // The membership IS the identity of the bearer of the secret: there is no guid in the
        // route, so listing anything but the caller's own child would be listing someone else's.
        Assert.Equal([path, child], HrefsOf(response));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnIntermediateCollection_RefusesAnInfiniteDepthLikeTheHome(bool principals)
    {
        var path = principals ? DavPaths.PrincipalCollection : DavPaths.BookCollection;

        var response = await Propfind(path, depth: null, body: null);

        // A collection, so RFC 4918 § 9.1's refusal applies exactly as it does to the home and the
        // book — one policy, not a third answer to the same silence.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "propfind-finite-depth", ConditionOf(response));
    }

    [Fact]
    public async Task TheBookCollection_AnnouncesNoReportAndCarriesNoSyncToken()
    {
        var response = await Propfind(DavPaths.BookCollection, depth: "0",
            body: PropBody("supported-report-set", "sync-token"));

        // Nothing is synchronised here and no report is served, so the announcement is empty and
        // sync-token comes back in the 404 propstat — which is what the tester reads as badprops.
        var document = XDocument.Parse(await response.ReadAsync());
        Assert.Empty(document.Descendants(DavXml.Dav + "supported-report-set").Single().Elements());
        var propstat404 = document.Descendants(DavXml.Dav + "propstat")
            .Single(p => p.Element(DavXml.Dav + "status")!.Value.Contains("404"));
        Assert.Single(propstat404.Descendants(DavXml.Dav + "sync-token"));
    }

    [Fact]
    public async Task AnotherUsersPrincipal_Answers404AndNot403()
    {
        var response = await Propfind(DavPaths.Principal(Guid.NewGuid()), depth: "0", body: null);

        // A 403 would confirm the existence of the principal aimed at.
        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task AnEmptyBody_MeansAllprop()
    {
        var response = await Propfind(DavPaths.Collection(UserId), depth: "0", body: null);

        // RFC 4918 § 9.1, and several clients send one at discovery. The VALUE is asserted, not
        // the element's presence: propname also emits an (empty) displayname element.
        Assert.Equal(207, response.StatusCode);
        var displayName = XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.Dav + "displayname").Single();
        Assert.Equal("Contacts", displayName.Value);
    }

    [Fact]
    public async Task ARequestedPropertyWeDoNotCarry_ComesBackIn404_AfterThe200()
    {
        var response = await Propfind(DavPaths.Collection(UserId), depth: "0",
            body: PropBody("displayname", "acl"));

        var body = await response.ReadAsync();
        Assert.True(body.IndexOf("200 OK", StringComparison.Ordinal)
                    < body.IndexOf("404 Not Found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAllpropBesideAPropname_Answers400()
    {
        var response = await Propfind(DavPaths.Collection(UserId), depth: "0",
            body: PropfindBody(new XElement(DavXml.Dav + "allprop"),
                new XElement(DavXml.Dav + "propname")));

        // RFC 4918 § 14.20 admits exactly one of the three. Taken as allprop — the fallback an
        // empty body earns — the answer would say the server understood a request it did not.
        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownChildOfPropfind_Answers400()
    {
        var response = await Propfind(DavPaths.Collection(UserId), depth: "0",
            body: PropfindBody(new XElement(DavXml.Dav + "undefined")));

        // The element carries no shape at all, so the 207 it used to earn was the empty body's
        // allprop under another name.
        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task AnIncludeWithoutItsAllprop_Answers400()
    {
        var response = await Propfind(DavPaths.Collection(UserId), depth: "0",
            body: PropfindBody(new XElement(DavXml.Dav + "include",
                new XElement(DavXml.Dav + "sync-token"))));

        // § 14.8 defines include as allprop's sibling and nowhere else; alone it names properties
        // nothing would ever read.
        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task APropfindWithNoChildAtAll_IsStillAllprop()
    {
        var response = await Propfind(DavPaths.Collection(UserId), depth: "0",
            body: PropfindBody());

        // Decision 14 of 4c: an empty body means allprop, and a root spelled out but left empty is
        // the same silence. The strictness must refuse shapes, never the absence of one.
        Assert.Equal(207, response.StatusCode);
        Assert.Equal("Contacts", XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.Dav + "displayname").Single().Value);
    }

    [Fact]
    public async Task ADtdInTheBody_Answers400()
    {
        var response = await Propfind(DavPaths.Collection(UserId), depth: "0",
            body: "<!DOCTYPE t [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><t>&x;</t>");

        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task APathNoRouteServes_IsAFourOhFourFromTheRouter()
    {
        // The harness's own worth rests on this: a request nothing matches must fall through the
        // router as 404, not dispatch anywhere — otherwise every routing assertion here is vacuous.
        Assert.Equal(404, (await Propfind("/dav/nowhere/", depth: "0", body: null)).StatusCode);
        Assert.Equal(404, (await Propfind($"/dav/addressbooks/not-a-guid/{DavPaths.BookName}/",
            depth: "0", body: null)).StatusCode);
    }

    private Task<DavTestResponse> Propfind(string path, string? depth, string? body) =>
        server.PropfindAsync(path, depth, body);

    private void GivenCards(params string[] names)
    {
        using var db = server.CreateContext();
        var sequence = (ulong)db.Contacts.Count();
        foreach (var name in names) SeedCard(db, UserId, name, ++sequence);
        db.SaveChanges();
    }

    private void GivenCardAtRank(string davName, ulong rank)
    {
        using var db = server.CreateContext();
        SeedCard(db, UserId, davName, rank);
        db.SaveChanges();
    }

    /// <summary>The counter alone, never followed by <see cref="GivenCardAtRank"/>: a test here
    /// pins the bound, so the seeded ranks must be free to sit on either side of it.</summary>
    private void GivenTheCounterAt(ulong seq)
    {
        using var db = server.CreateContext();
        db.ContactSyncStates.Add(new ContactSyncState
        {
            UserId = UserId, Epoch = Epoch, Seq = seq, PrunedBelow = 0
        });
        db.SaveChanges();
    }

    private static void SeedCard(PreferencesDbContext db, Guid userId, string davName, ulong rank)
    {
        var id = Guid.NewGuid();
        db.Contacts.Add(new Contact
        {
            Id = id,
            UserId = userId,
            Uid = id.ToString(),
            DavName = davName,
            VCardRaw = $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:{id}\r\nFN:{davName}\r\nEND:VCARD\r\n",
            CardHash = $"hash-of-{davName}",
            UpdatedAt = DateTime.UtcNow,
            SyncSequence = rank,
        });
    }

    /// <summary>Visible in the webmail, invisible to the protocol: no dav_name yet — the one
    /// condition that separates it from the cards <see cref="GivenCards"/> seeds.</summary>
    private void GivenACardWithNoName()
    {
        using var db = server.CreateContext();
        var id = Guid.NewGuid();
        db.Contacts.Add(new Contact
        {
            Id = id,
            UserId = UserId,
            Uid = id.ToString(),
            DavName = null,
            VCardRaw = $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:{id}\r\nFN:Unreachable\r\nEND:VCARD\r\n",
            CardHash = "hash-of-the-nameless",
            UpdatedAt = DateTime.UtcNow,
            SyncSequence = 999,
        });
        db.SaveChanges();
    }

    private static string PropBody(params string[] names) =>
        PropBody(names.Select(name => (XName)(DavXml.Dav + name)));

    private static string PropBody(string name, XNamespace ns) => PropBody([ns + name]);

    private static string PropfindBody(params XElement[] children) =>
        new XDocument(new XElement(DavXml.Dav + "propfind", children)).ToString();

    private static string PropBody(IEnumerable<XName> names) =>
        new XDocument(new XElement(DavXml.Dav + "propfind",
            new XElement(DavXml.Prop, names.Select(name => new XElement(name))))).ToString();

    private static XName ConditionOf(DavTestResponse response) =>
        XDocument.Parse(response.Body).Root!.Elements().Single().Name;

    private static List<XElement> ResponsesOf(DavTestResponse response) =>
        [.. XDocument.Parse(response.Body).Root!.Elements(DavXml.Response)];

    private static List<string> HrefsOf(DavTestResponse response) =>
        [.. ResponsesOf(response).Select(r => r.Element(DavXml.Href)!.Value)];

    private string CtagOf(DavTestResponse response) => CtagOf(response, DavPaths.Collection(UserId));

    /// <summary>The collection's own ctag, named by its href rather than taken as the first in the
    /// document: a card answers the request with an EMPTY getctag in its 404 propstat.</summary>
    private static string CtagOf(DavTestResponse response, string collectionHref) =>
        ResponsesOf(response)
            .Single(r => r.Element(DavXml.Href)!.Value == collectionHref)
            .Descendants(DavXml.CalendarServer + "getctag")
            .Single()
            .Value;

    /// <summary>
    /// Reads the state for real, but every read past the first answers a counter that has moved
    /// on. Registered where a test claims one read serves both halves of the answer: nothing else
    /// of this store is reachable from a PROPFIND, hence the refusals.
    /// </summary>
    private sealed class DriftingSyncStore(PreferencesDbContext context) : IContactSyncStore
    {
        private const ulong Drifted = 99;

        private int reads;

        public async Task<SyncState?> ReadStateAsync(Guid userId, CancellationToken cancellationToken)
        {
            var row = await context.ContactSyncStates
                .SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken);
            return row is null
                ? null
                : new SyncState(row.Epoch, reads++ == 0 ? row.Seq : Drifted, row.PrunedBelow);
        }

        public Task<ulong> NextSequenceAsync(Guid userId, CancellationToken cancellationToken) =>
            throw Refused();

        public Task<SyncState> ReadOrCreateStateAsync(Guid userId, CancellationToken cancellationToken) =>
            throw Refused();

        public Task PlaceTombstoneAsync(
            Guid userId, string davName, ulong sequence, CancellationToken cancellationToken) =>
            throw Refused();

        public Task LiftTombstoneAsync(Guid userId, string davName, CancellationToken cancellationToken) =>
            throw Refused();

        public Task<bool> ArchiveAsync(ContactRevision revision, CancellationToken cancellationToken) =>
            throw Refused();

        public Task<PruneOutcome> PruneAsync(DateTime tombstonesBefore, DateTime revisionsBefore,
            CancellationToken cancellationToken) =>
            throw Refused();

        private static InvalidOperationException Refused() =>
            new("A PROPFIND reads the state and nothing else of this store.");
    }
}
