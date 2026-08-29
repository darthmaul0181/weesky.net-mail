using System.Xml.Linq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class CardDavPropfindTests : IAsyncLifetime
{
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
    public async Task DepthZeroOnTheCollection_AnswersTheCollectionAlone()
    {
        var response = await Propfind(DavPaths.Collection(UserId), depth: "0", body: PropBody("displayname"));

        Assert.Equal(207, response.StatusCode);
        Assert.Single(ResponsesOf(response));
    }

    [Fact]
    public async Task DepthOneOnTheCollection_AnswersTheCollectionThenOneResponsePerCard()
    {
        GivenCards("a.vcf", "b.vcf");

        var response = await Propfind(DavPaths.Collection(UserId), depth: "1", body: PropBody("getetag"));

        // The collection comes first with its trailing slash, then every member under the EXACT
        // href a client will GET after discovery — a member href built any other way 404s on every
        // cycle, so the construction itself is pinned, not merely the count and the shape.
        Assert.Equal(
            [DavPaths.Collection(UserId), DavPaths.Card(UserId, "a.vcf"), DavPaths.Card(UserId, "b.vcf")],
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
        foreach (var name in names)
        {
            var id = Guid.NewGuid();
            db.Contacts.Add(new Contact
            {
                Id = id,
                UserId = UserId,
                Uid = id.ToString(),
                DavName = name,
                VCardRaw = $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:{id}\r\nFN:{name}\r\nEND:VCARD\r\n",
                CardHash = $"hash-of-{name}",
                UpdatedAt = DateTime.UtcNow,
                SyncSequence = ++sequence,
            });
        }

        db.SaveChanges();
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

    private static string PropBody(IEnumerable<XName> names) =>
        new XDocument(new XElement(DavXml.Dav + "propfind",
            new XElement(DavXml.Prop, names.Select(name => new XElement(name))))).ToString();

    private static XName ConditionOf(DavTestResponse response) =>
        XDocument.Parse(response.Body).Root!.Elements().Single().Name;

    private static List<XElement> ResponsesOf(DavTestResponse response) =>
        [.. XDocument.Parse(response.Body).Root!.Elements(DavXml.Response)];

    private static List<string> HrefsOf(DavTestResponse response) =>
        [.. ResponsesOf(response).Select(r => r.Element(DavXml.Href)!.Value)];
}
