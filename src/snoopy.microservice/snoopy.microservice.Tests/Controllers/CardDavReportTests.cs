using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class CardDavReportTests : IAsyncLifetime
{
    private DavTestServer server = null!;
    private ulong sequence;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync() => server = await DavTestServer.StartAsync();

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task AMultiget_AnswersOneResponsePerNamedHref()
    {
        GivenCards("a.vcf", "b.vcf");

        var response = await Report(DavPaths.Collection(UserId), MultigetBody(
            DavPaths.Card(UserId, "a.vcf"), DavPaths.Card(UserId, "b.vcf")));

        Assert.Equal(207, response.StatusCode);
        Assert.Equal(2, ResponsesOf(response).Count);
    }

    [Fact]
    public async Task AMultiget_ServesTheCardInAddressData()
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\nEND:VCARD\r\n");

        var response = await Report(DavPaths.Collection(UserId),
            MultigetBody(DavPaths.Card(UserId, "a.vcf"), withAddressData: true));

        Assert.Contains("FN:Ada", AddressDataOf(response).Single());
    }

    [Fact]
    public async Task AnAllpropMultiget_StillHonoursTheAddressDataItsIncludeNames()
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\nEND:VCARD\r\n");

        // Evolution's form: an allprop whose sibling include names address-data. Losing the
        // include would silently serve the properties and withhold the card itself.
        var response = await Report(DavPaths.Collection(UserId),
            AllpropIncludeMultigetBody(DavPaths.Card(UserId, "a.vcf")));

        Assert.Equal(207, response.StatusCode);
        Assert.Contains("FN:Ada", AddressDataOf(response).Single());
    }

    [Fact]
    public async Task TheResponses_FollowTheOrderOfTheBody_EachHrefCarryingItsOwnCard()
    {
        // Distinct content on purpose, and one name that needs escaping: fixtures whose names,
        // bytes and hashes are mutually derivable leave the pairing unpinned while looking thorough.
        GivenCard("a.vcf",
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\nEMAIL:ada@x.be\r\nEND:VCARD\r\n");
        GivenCard("plan #9.vcf", "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u2\r\nFN:Neuf\r\nEND:VCARD\r\n");

        // The body names them in the REVERSE of insertion order, which is what the database
        // answers: same-order output would pass by coincidence.
        var response = await Report(DavPaths.Collection(UserId), MultigetBody(
            [DavPaths.Card(UserId, "plan #9.vcf"), DavPaths.Card(UserId, "a.vcf")],
            withAddressData: true));

        Assert.Equal(
            [DavPaths.Card(UserId, "plan #9.vcf"), DavPaths.Card(UserId, "a.vcf")],
            HrefsOf(response));
        var served = AddressDataOf(response);
        Assert.Contains("FN:Neuf", served[0]);
        Assert.Contains("FN:Ada", served[1]);
    }

    [Fact]
    public async Task AMultiget_CarriesGetetagAlongsideAPartialAddressData()
    {
        GivenCard("a.vcf",
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:A\r\nEMAIL:a@b.c\r\nEND:VCARD\r\n",
            hash: "9f1c2d");

        var response = await Report(DavPaths.Collection(UserId),
            MultigetBody(DavPaths.Card(UserId, "a.vcf"), addressDataProps: ["EMAIL"]));

        // getetag is a PROPERTY OF THE RESOURCE, not the fingerprint of the body this propstat
        // carries: it is the value the client files to know, next time round, whether to re-read.
        var etag = XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.Dav + "getetag").Single();
        Assert.Equal("\"9f1c2d\"", etag.Value);
        Assert.DoesNotContain("FN:A", AddressDataOf(response).Single());
        Assert.Contains("EMAIL:a@b.c", AddressDataOf(response).Single());
    }

    [Fact]
    public async Task AMultiget_ConvertsWhenAVersionIsAsked()
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:A\r\nEND:VCARD\r\n");

        var response = await Report(DavPaths.Collection(UserId),
            MultigetBody(DavPaths.Card(UserId, "a.vcf"), version: "4.0"));

        // DAVx5 asks for version="4.0" as soon as the announcement carries 4.0. Serving as stored
        // would replay sabre's 2013 regression.
        Assert.Contains("VERSION:4.0", AddressDataOf(response).Single());
    }

    [Fact]
    public async Task AVersionAskedWithAPartialSet_ConvertsBeforeItRestricts()
    {
        GivenCard("a.vcf",
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\nEMAIL:ada@x.be\r\nEND:VCARD\r\n");

        var response = await Report(DavPaths.Collection(UserId),
            MultigetBody(DavPaths.Card(UserId, "a.vcf"), addressDataProps: ["EMAIL"],
                version: "4.0"));

        // Restriction is textual: restricted first, the conversion would re-insert the FN the
        // library considers mandatory, silently undoing the restriction.
        var served = AddressDataOf(response).Single();
        Assert.Contains("VERSION:4.0", served);
        Assert.Contains("EMAIL", served);
        Assert.DoesNotContain("FN:", served);
    }

    [Fact]
    public async Task AnAddressDataVersionWeDoNotAnnounce_IsRefusedWithSupportedAddressData()
    {
        GivenCards("a.vcf");

        var response = await Report(DavPaths.Collection(UserId),
            MultigetBody(DavPaths.Card(UserId, "a.vcf"), version: "5.0"));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CardDav + "supported-address-data", ConditionOf(response));
    }

    [Fact]
    public async Task AnUnknownHref_Answers404InsideTheMultistatus()
    {
        GivenCards("a.vcf");

        var response = await Report(DavPaths.Collection(UserId), MultigetBody(
            DavPaths.Card(UserId, "a.vcf"), DavPaths.Card(UserId, "gone.vcf")));

        // The report is a batch read, and a stale name in a client's list is a common case, not a
        // fault. A global error would throw away the card that WAS found.
        Assert.Equal(207, response.StatusCode);
        Assert.Contains("404 Not Found", await response.ReadAsync());
        Assert.Equal(2, ResponsesOf(response).Count);
    }

    [Fact]
    public async Task AnHrefOutsideThisCollection_IsAlso404AndDoesNotLeakTheForeignCard()
    {
        GivenCards("a.vcf");
        var foreignUser = Guid.NewGuid();
        GivenAForeignCard(foreignUser, "x.vcf");

        var response = await Report(DavPaths.Collection(UserId),
            MultigetBody($"/dav/addressbooks/{foreignUser}/{DavPaths.BookName}/x.vcf",
                withAddressData: true));

        // The card EXISTS under that very href — for another user. Anything but a bare 404 leaks it.
        Assert.Equal(207, response.StatusCode);
        Assert.Contains("404 Not Found", await response.ReadAsync());
        Assert.Empty(AddressDataOf(response));
    }

    [Fact]
    public async Task AnHrefOutsideThisCollection_IsNeverEvenLookedUp()
    {
        await using var guarded = await DavTestServer.StartAsync(
            overrides: services => services.AddScoped<IDavContactReader, RefusingReader>());

        var response = await guarded.SendAsync("REPORT", DavPaths.Collection(guarded.UserId),
            MultigetBody("/dav/addressbooks/" + Guid.NewGuid() + $"/{DavPaths.BookName}/x.vcf"));

        // The reader throws on ANY call: the 207 is only reachable if the foreign href never
        // produced a read at all.
        Assert.Equal(207, response.StatusCode);
        Assert.Contains("404 Not Found", response.Body);
    }

    [Fact]
    public async Task MoreThanFiveThousandHrefs_AnswersTheTruncationShape()
    {
        var body = MultigetBody([.. Enumerable.Range(0, MultigetReport.MaxHrefs + 1)
            .Select(i => DavPaths.Card(UserId, $"c{i}.vcf"))]);

        var response = await Report(DavPaths.Collection(UserId), body);

        // Not a bare 403, which rests on no text: the shape clients already read (RFC 6352 § 8.6.2).
        Assert.Equal(207, response.StatusCode);
        var responses = XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.Dav + "response").ToList();
        var onRequestUri = responses.Single(r =>
            r.Element(DavXml.Dav + "href")!.Value == DavPaths.Collection(UserId));
        Assert.Contains("507", onRequestUri.Element(DavXml.Dav + "status")!.Value);
        Assert.Single(onRequestUri.Descendants(DavXml.Dav + "number-of-matches-within-limits"));
    }

    [Fact]
    public async Task TheBound_IsJudgedBeforeASingleRead()
    {
        await using var guarded = await DavTestServer.StartAsync(
            overrides: services => services.AddScoped<IDavContactReader, RefusingReader>());

        var body = MultigetBody([.. Enumerable.Range(0, MultigetReport.MaxHrefs + 1)
            .Select(i => DavPaths.Card(guarded.UserId, $"c{i}.vcf"))]);
        var response = await guarded.SendAsync("REPORT", DavPaths.Collection(guarded.UserId), body);

        // Same throwing reader: the truncation shape is only reachable when the count refused the
        // batch before any resolution began.
        Assert.Equal(207, response.StatusCode);
        Assert.Contains("number-of-matches-within-limits", response.Body);
    }

    [Fact]
    public async Task AMultigetOnACard_IsServed()
    {
        GivenCards("a.vcf");

        // RFC 6352 § 8.7 defines multiget on address resources too, and supported-report-set says
        // so on each card — the routes must follow, or the header lies.
        var response = await Report(DavPaths.Card(UserId, "a.vcf"),
            MultigetBody(DavPaths.Card(UserId, "a.vcf")));

        // The card's OWN response, not merely a status: this route exists because the header
        // promises it, and a 207 carrying anything else would keep the promise in name only.
        Assert.Equal(207, response.StatusCode);
        Assert.Equal([DavPaths.Card(UserId, "a.vcf")], HrefsOf(response));
        Assert.Equal("\"hash-of-a.vcf\"", XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.Dav + "getetag").Single().Value);
    }

    [Fact]
    public async Task ExpandProperty_ResolvesAnHrefPropertyIntoANestedResponse()
    {
        var response = await Report(DavPaths.Principal(UserId), ExpandPropertyBody(
            DavXml.CardDav + "addressbook-home-set", DavXml.Dav + "displayname"));

        // iOS exercises this at principal discovery; it is a double MUST, and refusing it would be
        // a divergence on the very first request of the pairing.
        Assert.Equal(207, response.StatusCode);
        var nested = XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.CardDav + "addressbook-home-set")
            .Descendants(DavXml.Dav + "response").Single();
        Assert.Equal(DavPaths.Home(UserId), nested.Element(DavXml.Dav + "href")!.Value);
        // The nested properties are RESOLVED on the target, not echoed: the home's own displayname.
        Assert.Equal("Address Books",
            nested.Descendants(DavXml.Dav + "displayname").Single().Value);
    }

    [Fact]
    public async Task ExpandProperty_AnswersANested404ForATargetItCannotResolve()
    {
        // principal-collection-set hrefs "/dav/principals/", which resolves to no resource of
        // ours: the nested response must SAY 404, or an unresolvable target masquerades as found.
        var response = await Report(DavPaths.Principal(UserId), ExpandPropertyBody(
            DavXml.Dav + "principal-collection-set", DavXml.Dav + "displayname"));

        Assert.Equal(207, response.StatusCode);
        var nested = XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.Dav + "principal-collection-set")
            .Descendants(DavXml.Dav + "response").Single();
        Assert.Equal(DavPaths.PrincipalCollection, nested.Element(DavXml.Dav + "href")!.Value);
        Assert.Equal("HTTP/1.1 404 Not Found", nested.Element(DavXml.Dav + "status")!.Value);
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("a:b")]
    [InlineData("1bad")]
    public async Task AMalformedExpandPropertyName_Answers400AndNever500(string name)
    {
        // The one client string of this surface reaching XName construction without the parser's
        // own validation: escaping as a 500 makes a probe loop on the report iOS opens with.
        var response = await Report(DavPaths.Principal(UserId),
            $"<D:expand-property xmlns:D=\"DAV:\"><D:property name=\"{name}\"/></D:expand-property>");

        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task AMalformedNestedExpandPropertyName_Answers400Too()
    {
        var body = "<D:expand-property xmlns:D=\"DAV:\">" +
            "<D:property name=\"addressbook-home-set\" namespace=\"urn:ietf:params:xml:ns:carddav\">" +
            "<D:property name=\"not valid\"/></D:property></D:expand-property>";

        var response = await Report(DavPaths.Principal(UserId), body);

        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task AReportOnTheHome_IsAConsideredRefusalNotA405()
    {
        // The home's Allow names REPORT: a 405 there is a header that lies, and an RFC 9110
        // client retries the verb for ever. The default branch answers instead.
        var response = await Report(DavPaths.Home(UserId), ReportBody("sync-collection"));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "supported-report", ConditionOf(response));
    }

    [Fact]
    public async Task AReportOnTheServiceRoot_IsAConsideredRefusalNotA405()
    {
        var response = await Report("/dav/", ReportBody("sync-collection"));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "supported-report", ConditionOf(response));
    }

    [Fact]
    public async Task ExpandPropertyOnTheServiceRoot_ResolvesCurrentUserPrincipal()
    {
        var response = await Report("/dav/", ExpandPropertyBody(
            DavXml.Dav + "current-user-principal", DavXml.Dav + "displayname"));

        Assert.Equal(207, response.StatusCode);
        var nested = XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.Dav + "current-user-principal")
            .Descendants(DavXml.Dav + "response").Single();
        Assert.Equal(DavPaths.Principal(UserId), nested.Element(DavXml.Dav + "href")!.Value);
    }

    [Theory]
    [InlineData("addressbook-query")]
    [InlineData("sync-collection")]
    public async Task AReportThisPlanDoesNotYetServe_Answers403SupportedReport(string localName)
    {
        var response = await Report(DavPaths.Collection(UserId), ReportBody(localName));

        // Named and refused rather than left to fall through: a 500 makes a client loop for ever
        // on a report it believes temporarily broken. Plan c replaces the refusal by an
        // implementation.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "supported-report", ConditionOf(response));
    }

    [Fact]
    public async Task AnUnknownReport_Answers403SupportedReportToo()
    {
        var response = await Report(DavPaths.Collection(UserId), ReportBody("acl-principal-prop-set"));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "supported-report", ConditionOf(response));
    }

    [Fact]
    public async Task AnEmptyBody_NamesNoReportAndIsRefusedTheSameWay()
    {
        var response = await Report(DavPaths.Collection(UserId), body: null);

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "supported-report", ConditionOf(response));
    }

    [Fact]
    public async Task AnotherUsersCollection_Answers404BeforeAnythingElse()
    {
        var response = await Report(DavPaths.Collection(Guid.NewGuid()),
            MultigetBody(DavPaths.Card(Guid.NewGuid(), "a.vcf")));

        // A 403 would confirm the existence of the book aimed at.
        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task ADepthHeaderOnAReport_IsIgnoredRatherThanRefused()
    {
        GivenCards("a.vcf");

        // PROPFIND's rule is PROPFIND's alone: a report already says what it applies to, so there
        // is nothing to guess. Extending the refusal here would break all three reports.
        var response = await Report(DavPaths.Collection(UserId),
            MultigetBody(DavPaths.Card(UserId, "a.vcf")), depth: "infinity");

        Assert.Equal(207, response.StatusCode);
    }

    [Fact]
    public async Task ABodyOverOneMegabyte_Answers413()
    {
        var response = await Report(DavPaths.Collection(UserId), OversizedBody());

        Assert.Equal(413, response.StatusCode);
    }

    private Task<DavTestResponse> Report(string path, string? body, string? depth = null) =>
        server.SendAsync("REPORT", path, body, depth);

    private void GivenCards(params string[] names)
    {
        foreach (var name in names)
        {
            GivenCard(name,
                $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u-{name}\r\nFN:{name}\r\nEND:VCARD\r\n");
        }
    }

    private void GivenCard(string davName, string vCard, string? hash = null) =>
        Seed(UserId, davName, vCard, hash);

    private void GivenAForeignCard(Guid owner, string davName) =>
        Seed(owner, davName,
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:foreign\r\nFN:Foreign\r\nEND:VCARD\r\n", null);

    private void Seed(Guid owner, string davName, string vCard, string? hash)
    {
        using var db = server.CreateContext();
        var id = Guid.NewGuid();
        db.Contacts.Add(new Contact
        {
            Id = id,
            UserId = owner,
            Uid = id.ToString(),
            DavName = davName,
            VCardRaw = vCard,
            CardHash = hash ?? $"hash-of-{davName}",
            UpdatedAt = DateTime.UtcNow,
            SyncSequence = ++sequence,
        });
        db.SaveChanges();
    }

    private static string MultigetBody(params string[] hrefs) =>
        MultigetBody(hrefs, false, null, null);

    private static string MultigetBody(string href, bool withAddressData = false,
        string[]? addressDataProps = null, string? version = null) =>
        MultigetBody([href], withAddressData, addressDataProps, version);

    private static string MultigetBody(string[] hrefs, bool withAddressData = false,
        string[]? addressDataProps = null, string? version = null)
    {
        var prop = new XElement(DavXml.Prop, new XElement(DavXml.Dav + "getetag"));
        if (withAddressData || addressDataProps is not null || version is not null)
        {
            var addressData = new XElement(DavXml.CardDav + "address-data");
            if (version is not null)
            {
                addressData.Add(new XAttribute("content-type", "text/vcard"),
                    new XAttribute("version", version));
            }

            foreach (var name in addressDataProps ?? [])
                addressData.Add(new XElement(DavXml.CardDav + "prop", new XAttribute("name", name)));
            prop.Add(addressData);
        }

        return new XDocument(new XElement(DavXml.CardDav + "addressbook-multiget", prop,
            hrefs.Select(href => new XElement(DavXml.Href, href)))).ToString();
    }

    private static string AllpropIncludeMultigetBody(string href) =>
        new XDocument(new XElement(DavXml.CardDav + "addressbook-multiget",
            new XElement(DavXml.Dav + "allprop"),
            new XElement(DavXml.Dav + "include", new XElement(DavXml.CardDav + "address-data")),
            new XElement(DavXml.Href, href))).ToString();

    private static string ExpandPropertyBody(XName outer, XName inner) =>
        new XDocument(new XElement(DavXml.Dav + "expand-property",
            new XElement(DavXml.Dav + "property",
                new XAttribute("name", outer.LocalName),
                new XAttribute("namespace", outer.NamespaceName),
                new XElement(DavXml.Dav + "property",
                    new XAttribute("name", inner.LocalName),
                    new XAttribute("namespace", inner.NamespaceName))))).ToString();

    private static string ReportBody(string localName)
    {
        var ns = localName.StartsWith("addressbook", StringComparison.Ordinal)
            ? DavXml.CardDav
            : DavXml.Dav;
        return new XDocument(new XElement(ns + localName, new XElement(DavXml.Prop))).ToString();
    }

    /// <summary>Just past the 1 MB the routes announce; well-formed, so only the size refuses it.</summary>
    private static string OversizedBody() =>
        "<oversized>" + new string('a', 1024 * 1024) + "</oversized>";

    private static XName ConditionOf(DavTestResponse response) =>
        XDocument.Parse(response.Body).Root!.Elements().Single().Name;

    private static List<XElement> ResponsesOf(DavTestResponse response) =>
        [.. XDocument.Parse(response.Body).Root!.Elements(DavXml.Response)];

    private static List<string> HrefsOf(DavTestResponse response) =>
        [.. ResponsesOf(response).Select(r => r.Element(DavXml.Href)!.Value)];

    private static List<string> AddressDataOf(DavTestResponse response) =>
        [.. XDocument.Parse(response.Body)
            .Descendants(DavXml.CardDav + "address-data").Select(e => e.Value)];

    /// <summary>Throws on every member: registered where a test claims no read may happen.</summary>
    private sealed class RefusingReader : IDavContactReader
    {
        public IAsyncEnumerable<DavCard> StreamAsync(Guid userId, CancellationToken cancellationToken) =>
            throw Refused();

        public Task<DavCard?> FindAsync(Guid userId, string davName, CancellationToken cancellationToken) =>
            throw Refused();

        public Task<IReadOnlyList<DavCard>> FindManyAsync(
            Guid userId, IReadOnlyList<string> davNames, CancellationToken cancellationToken) =>
            throw Refused();

        public Task<int> CountAsync(Guid userId, CancellationToken cancellationToken) =>
            throw Refused();

        private static InvalidOperationException Refused() =>
            new("This test promised the repository would never be read.");
    }
}
