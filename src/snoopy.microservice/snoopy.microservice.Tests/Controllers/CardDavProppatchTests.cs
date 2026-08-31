using System.Xml.Linq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// PROPPATCH is the only non-mutating method of this slice that is NOT a 405. The DAV: 1 header
/// engages — RFC 4918 § 18.1 makes class 1 the satisfaction of every MUST of the document, and
/// § 9.2 requires PROPPATCH of every conforming resource — and Apple's Contacts.app PROPPATCHes
/// {calendarserver}me-card on the address HOME, which sabre documents can crash the client when it
/// is unsupported. The answer everywhere is § 9.2.1's for a property one does not let write: a 207
/// whose every propstat carries 403 Forbidden, and nothing stored on the way through.
/// </summary>
public sealed class CardDavProppatchTests : IAsyncLifetime
{
    private const string CardName = "a.vcf";

    private DavTestServer server = null!;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync();
        GivenCard(CardName);
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Theory]
    [InlineData("root")]
    [InlineData("principal")]
    [InlineData("home")]
    [InlineData("collection")]
    [InlineData("card")]
    public async Task Proppatch_Answers207_Everywhere(string target)
    {
        var response = await Proppatch(UrlOf(target), SetBody(DavXml.Dav + "displayname", "X"));

        // DAV: 1 engages: § 18.1 makes class 1 the satisfaction of every MUST, and § 9.2 requires
        // PROPPATCH of every conforming resource. Answering 405 is a contradiction a conformance
        // test catches on its first pass.
        Assert.Equal(207, response.StatusCode);
    }

    [Fact]
    public async Task Proppatch_OnTheBareRootOutsideDav_IsAnsweredToo()
    {
        var response = await Proppatch("/", SetBody(DavXml.Dav + "displayname", "X"));

        // A client given the bare host tries "/" as much as the well-known, and this is the one
        // route of the six whose URL the theory above cannot spell.
        Assert.Equal(207, response.StatusCode);
        Assert.Equal("/", XDocument.Parse(response.Body).Descendants(DavXml.Href).Single().Value);
    }

    [Fact]
    public async Task Proppatch_RefusesEachPropertyIn403()
    {
        var response = await Proppatch(DavPaths.Collection(UserId),
            SetBody(DavXml.Dav + "displayname", "X"));

        var propstat = XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.Dav + "propstat").Single();
        Assert.Contains("403 Forbidden", propstat.Element(DavXml.Dav + "status")!.Value);
        Assert.Single(propstat.Element(DavXml.Dav + "prop")!.Elements(DavXml.Dav + "displayname"));
    }

    [Fact]
    public async Task TheStatusLine_IsTheLiteralOneClientsCompareByteForByte()
    {
        var response = await Proppatch(DavPaths.Collection(UserId),
            SetBody(DavXml.Dav + "displayname", "X"));

        // Not the framework's reason-phrase table, which has already changed case between versions
        // while at least one client compares this line as a string.
        Assert.Equal("HTTP/1.1 403 Forbidden", XDocument.Parse(response.Body)
            .Descendants(DavXml.PropStat).Single().Element(DavXml.Status)!.Value);
    }

    [Fact]
    public async Task Proppatch_OfMeCardOnTheHome_IsAnsweredAndNotCrashed()
    {
        var response = await Proppatch(DavPaths.Home(UserId),
            SetBody(DavXml.CalendarServer + "me-card", "/dav/addressbooks/x/default/a.vcf"));

        // Contacts.app writes it HERE, on the address home, and sabre documents that not supporting
        // it can make the client CRASH — not abandon the book, crash.
        Assert.Equal(207, response.StatusCode);
        Assert.Single(XDocument.Parse(response.Body)
            .Descendants(DavXml.Prop).Single().Elements(DavXml.CalendarServer + "me-card"));
    }

    [Fact]
    public async Task Proppatch_StoresNothing()
    {
        await Proppatch(DavPaths.Collection(UserId), SetBody(DavXml.Dav + "displayname", "Renamed"));

        var after = await Propfind(DavPaths.Collection(UserId), "0", PropBody("displayname"));
        // Served does not mean stored: accepting me-card would want one more dead property in the
        // database, for a use no screen of the product renders.
        Assert.DoesNotContain("Renamed", await after.ReadAsync());
    }

    [Fact]
    public async Task Proppatch_LeavesTheStoredRowsUntouched()
    {
        var before = SnapshotOfTheBook();

        await Proppatch(DavPaths.Card(UserId, CardName),
            SetBody(DavXml.Dav + "displayname", "Renamed"));
        await Proppatch(DavPaths.Collection(UserId), RemoveBody(DavXml.Dav + "displayname"));
        await Proppatch(DavPaths.Home(UserId),
            SetBody(DavXml.CalendarServer + "me-card", DavPaths.Card(UserId, CardName)));

        // The negative, proved over the rows themselves rather than over one served property —
        // which cannot prove it, the collection's displayname being a constant. These are the four
        // tables the two stores this controller holds can write: a sequence bumped, a tombstone
        // placed, a revision archived or a card rewritten all move a byte here.
        Assert.Equal(before, SnapshotOfTheBook());
    }

    [Fact]
    public async Task Proppatch_NamesEveryPropertyTheBodyAsked()
    {
        var response = await Proppatch(DavPaths.Collection(UserId),
            SetBody(DavXml.Dav + "displayname", "X", DavXml.CardDav + "addressbook-description", "Y"));

        Assert.Equal(2, XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.Dav + "prop").Single().Elements().Count());
    }

    [Fact]
    public async Task Proppatch_ReadsRemoveAsWellAsSet()
    {
        var response = await Proppatch(DavPaths.Collection(UserId),
            SetAndRemoveBody(DavXml.Dav + "displayname", DavXml.CardDav + "addressbook-description"));

        // § 9.2 names both in one document. Reading only DAV:set answers a client that its removal
        // succeeded — the propstat it never received.
        var named = XDocument.Parse(response.Body).Descendants(DavXml.Prop).Single().Elements()
            .Select(element => element.Name).ToList();
        Assert.Contains(DavXml.Dav + "displayname", named);
        Assert.Contains(DavXml.CardDav + "addressbook-description", named);
    }

    [Fact]
    public async Task Proppatch_AnswersTheComplianceClasses()
    {
        var response = await Proppatch(DavPaths.Collection(UserId),
            SetBody(DavXml.Dav + "displayname", "X"));

        // The 405 catch-all carries an Allow and no DAV header: this asserts the answer came from
        // the action rather than from the route that used to swallow this verb.
        Assert.Equal(DavHeaders.ComplianceClasses, response.Header("DAV"));
        Assert.Null(response.Header("Allow"));
    }

    [Fact]
    public async Task Proppatch_OnAForeignPrincipal_Answers404()
    {
        var response = await Proppatch(DavPaths.Collection(Guid.NewGuid()),
            SetBody(DavXml.Dav + "displayname", "X"));

        // Never 403: a 403 would confirm the existence of the principal aimed at.
        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task Proppatch_OnACardThatDoesNotExist_Answers404()
    {
        var response = await Proppatch(DavPaths.Card(UserId, "missing.vcf"),
            SetBody(DavXml.Dav + "displayname", "X"));

        // A 207 here would tell the client the card exists — the same lie a PROPFIND refuses to
        // tell on a name that designates nothing.
        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task Proppatch_OnACollectionUrlMissingItsSlash_Redirects308()
    {
        var response = await Proppatch(DavPaths.Collection(UserId).TrimEnd('/'),
            SetBody(DavXml.Dav + "displayname", "X"));

        // 308, never 301: a 301 lets the client replay as GET, and a redirected PROPPATCH would
        // lose both its method and its body.
        Assert.Equal(308, response.StatusCode);
        Assert.Equal(DavPaths.Collection(UserId), response.Header("Location"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("<not-a-propertyupdate/>")]
    [InlineData("<propertyupdate")]
    public async Task ABodyThatIsNotAPropertyUpdate_Answers400(string? body)
    {
        var response = await Proppatch(DavPaths.Collection(UserId), body);

        // § 9.2 requires the body; a 500 is what a client retries forever, on the same resource.
        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task APropertyUpdateNamingNothing_IsStillA207()
    {
        var response = await Proppatch(DavPaths.Collection(UserId),
            new XDocument(new XElement(DavXml.Dav + "propertyupdate")).ToString());

        // Nothing was refused because nothing was asked: a response carrying no propstat, not a
        // 400 over a document that is otherwise what § 9.2 describes.
        Assert.Equal(207, response.StatusCode);
        Assert.Empty(XDocument.Parse(response.Body).Descendants(DavXml.PropStat));
    }

    [Fact]
    public async Task AnEmptySetProp_AnswersAResponseCarryingItsOwnStatus()
    {
        var body = new XDocument(new XElement(DavXml.Dav + "propertyupdate",
            new XElement(DavXml.Dav + "set", new XElement(DavXml.Prop)))).ToString();

        var response = await Proppatch(DavPaths.Collection(UserId), body);

        // Grammatically valid and naming nothing, so no propstat is written — and § 14.24 admits
        // (href, status) or (href, propstat+): the href alone is the shape a sync response was
        // just cured of, and this path reaches it on all seven of the surface's URLs.
        Assert.Equal(207, response.StatusCode);
        var written = XDocument.Parse(response.Body).Root!.Elements(DavXml.Response).Single();
        Assert.Equal("HTTP/1.1 200 OK", written.Elements(DavXml.Status).Single().Value);
        Assert.Empty(written.Elements(DavXml.PropStat));
    }

    [Fact]
    public async Task ABodyPastTheAnnouncedLimit_Answers413()
    {
        var response = await Proppatch(DavPaths.Collection(UserId),
            "<oversized>" + new string('a', 1024 * 1024) + "</oversized>");

        Assert.Equal(413, response.StatusCode);
    }

    private Task<DavTestResponse> Proppatch(string path, string? body) =>
        server.SendAsync("PROPPATCH", path, body);

    private Task<DavTestResponse> Propfind(string path, string? depth, string? body) =>
        server.PropfindAsync(path, depth, body);

    private string UrlOf(string target) => target switch
    {
        "root" => DavPaths.Root + "/",
        "principal" => DavPaths.Principal(UserId),
        "home" => DavPaths.Home(UserId),
        "collection" => DavPaths.Collection(UserId),
        _ => DavPaths.Card(UserId, CardName),
    };

    private void GivenCard(string davName)
    {
        using var db = server.CreateContext();
        var id = Guid.NewGuid();
        db.Contacts.Add(new Contact
        {
            Id = id,
            UserId = UserId,
            Uid = id.ToString(),
            DavName = davName,
            VCardRaw = $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:{id}\r\nFN:{davName}\r\nEND:VCARD\r\n",
            CardHash = $"hash-of-{davName}",
            UpdatedAt = DateTime.UtcNow,
            SyncSequence = 1,
        });
        db.SaveChanges();
    }

    /// <summary>Every row a PROPPATCH could conceivably land in, rendered as strings: a store
    /// written behind the served properties still moves a byte here.</summary>
    private List<string> SnapshotOfTheBook()
    {
        using var db = server.CreateContext();
        return
        [
            .. db.Contacts.AsEnumerable().Select(contact => string.Join('|',
                contact.Id, contact.Uid, contact.DavName, contact.VCardRaw, contact.CardHash,
                contact.UpdatedAt.Ticks, contact.SyncSequence)),
            .. db.ContactSyncStates.AsEnumerable().Select(state => string.Join('|',
                state.UserId, state.Seq, state.Epoch, state.PrunedBelow)),
            .. db.ContactTombstones.AsEnumerable().Select(tombstone => string.Join('|',
                tombstone.UserId, tombstone.DavName, tombstone.SyncSequence)),
            .. db.ContactRevisions.AsEnumerable().Select(revision => string.Join('|',
                revision.Id, revision.ContactId, revision.CardHash, revision.Cause)),
        ];
    }

    private static string PropBody(params string[] names) =>
        new XDocument(new XElement(DavXml.Dav + "propfind",
            new XElement(DavXml.Prop, names.Select(name => new XElement(DavXml.Dav + name)))))
            .ToString();

    private static string SetBody(XName name, string value) => Update([(name, value)], []);

    private static string SetBody(XName first, string firstValue, XName second, string secondValue) =>
        Update([(first, firstValue), (second, secondValue)], []);

    private static string RemoveBody(XName name) => Update([], [name]);

    private static string SetAndRemoveBody(XName set, XName removed) =>
        Update([(set, "X")], [removed]);

    private static string Update((XName Name, string Value)[] set, XName[] removed)
    {
        var root = new XElement(DavXml.Dav + "propertyupdate");
        if (set.Length > 0)
        {
            root.Add(new XElement(DavXml.Dav + "set", new XElement(DavXml.Prop,
                set.Select(pair => new XElement(pair.Name, pair.Value)))));
        }

        if (removed.Length > 0)
        {
            root.Add(new XElement(DavXml.Dav + "remove", new XElement(DavXml.Prop,
                removed.Select(name => new XElement(name)))));
        }

        return new XDocument(root).ToString();
    }
}
