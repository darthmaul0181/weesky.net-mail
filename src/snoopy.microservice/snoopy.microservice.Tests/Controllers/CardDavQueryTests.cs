using System.Xml.Linq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class CardDavQueryTests : IAsyncLifetime
{
    private DavTestServer server = null!;
    private ulong sequence;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync() => server = await DavTestServer.StartAsync();

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task AQueryWithAnEmptyFilter_AnswersTheWholeBook()
    {
        GivenCards("a.vcf", "b.vcf");

        var response = await Report(DavPaths.Collection(UserId), QueryBody(EmptyFilter()));

        Assert.Equal(2, ResponsesOf(response).Count);
    }

    [Fact]
    public async Task AQueryWithNoFilterElementAtAll_Answers400()
    {
        var response = await Report(DavPaths.Collection(UserId), QueryBodyWithoutFilter());

        // An incomplete request, not an unevaluable filter: 403 supported-filter would lie about
        // what is missing. The neighbouring rule says the opposite for an EMPTY filter, which is
        // exactly why the two are written side by side.
        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task AQuery_ReturnsOnlyTheMatchingCards()
    {
        GivenCardNamed("a.vcf", fn: "Ada Lovelace");
        GivenCardNamed("b.vcf", fn: "Grace Hopper");

        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(Filter(PropFilter("FN", TextMatch("Lovelace")))));

        Assert.Single(ResponsesOf(response));
        Assert.Contains(HrefsOf(response), h => h.EndsWith("a.vcf"));
    }

    [Fact]
    public async Task ACardMatchingOnlyInASiblingTableProperty_IsStillReturned()
    {
        // EMAIL, TEL and ADR live in tables no Expression<Func<Contact, bool>> can reach, so the
        // pre-filter gives up and the whole book is parsed. Should it ever narrow on display_name
        // instead, this card — whose FN matches nothing — vanishes from a perfectly ordinary
        // search, silently and with a 207.
        GivenCardNamed("a.vcf", fn: "Grace Hopper", email: "ada@lovelace.example");
        GivenCardNamed("b.vcf", fn: "Ada Lovelace", email: "grace@hopper.example");

        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(Filter(PropFilter("EMAIL", TextMatch("ada@lovelace")))));

        Assert.Equal([DavPaths.Card(UserId, "a.vcf")], HrefsOf(response));
    }

    [Fact]
    public async Task ACardWhoseSecondFnAloneMatches_IsStillReturned()
    {
        // vCard 4.0 gives FN cardinality 1*, the write path stores the card byte for byte, and
        // VCardProjector fills display_name from the FIRST FN. A SQL clause narrowing on that
        // column dropped this card before the exact evaluation ever saw it: a DAVx5 search for
        // "Lovelace" missing a contact, under a 207, with no error anywhere. That is why no clause
        // narrows this report any more — a column holding one instance of a repeatable property
        // cannot carry a pre-filter, and there is no column saying a card has a second FN.
        GivenTwoFnCard("a.vcf", "Grace Hopper", "Ada Lovelace");

        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(Filter(PropFilter("FN", TextMatch("Lovelace")))));

        Assert.Equal([DavPaths.Card(UserId, "a.vcf")], HrefsOf(response));
    }

    [Fact]
    public async Task TheBoundCountsMatchesOnly_SoAnExcludedCardNeverForgesATruncation()
    {
        // The non-matching card comes LAST, which is the only order that exposes the fault: a bound
        // tested BEFORE the exact evaluation counts a card the filter excludes as the one past the
        // limit, and answers a complete result set with a 507 and
        // number-of-matches-within-limits. A client told its answer was truncated re-queries for
        // ever, and no test combining a bound with a filter existed to say so.
        GivenCardNamed("a.vcf", fn: "Ada Lovelace");
        GivenCardNamed("b.vcf", fn: "Grace Lovelace");
        GivenCardNamed("c.vcf", fn: "Alan Turing");

        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(Filter(PropFilter("FN", TextMatch("Lovelace"))), carddavLimit: 2));

        Assert.Equal(2, ResponsesOfStatus(response, 200).Count);
        Assert.Empty(ResponsesOfStatus(response, 507));
    }

    [Fact]
    public async Task ACardWhoseDisplayNameColumnDisagreesWithItsCard_IsDecidedByTheCard()
    {
        // display_name is null when the projector rebuilt the FN, and holds a 255-character prefix
        // of a longer one. Both rows are answered by the card itself — the guard any future
        // narrowing clause would have to satisfy before it could be reintroduced.
        GivenCardNamed("null-column.vcf", fn: "Ada Lovelace", displayName: null);
        GivenCardNamed("capped.vcf", fn: new string('x', 254) + "Ada Lovelace",
            displayName: new string('x', 255));

        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(Filter(PropFilter("FN", TextMatch("Lovelace")))));

        Assert.Equal(
            [DavPaths.Card(UserId, "capped.vcf"), DavPaths.Card(UserId, "null-column.vcf")],
            HrefsOf(response).Order());
    }

    [Fact]
    public async Task AQuery_ServesAddressDataWhenAsked()
    {
        GivenCardNamed("a.vcf", fn: "Ada Lovelace");

        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(EmptyFilter(), withAddressData: true));

        Assert.Contains("FN:Ada Lovelace", AddressDataOf(response).Single());
    }

    [Fact]
    public async Task AQuery_HonoursAPartialAddressDataAndStillCarriesGetetag()
    {
        GivenCardNamed("a.vcf", fn: "Ada Lovelace", email: "a@b.c");

        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(EmptyFilter(), addressDataProps: ["EMAIL"]));

        // Returning the whole card would be the silent version of the same defect, with one more
        // consequence: the client would write a COMPLETE card into a cache it believes partial, and
        // rewrite it as such.
        Assert.DoesNotContain("FN:", AddressDataOf(response).Single());
        Assert.Contains("EMAIL:a@b.c", AddressDataOf(response).Single());
        Assert.NotEmpty(XDocument.Parse(await response.ReadAsync()).Descendants(DavXml.Dav + "getetag"));
    }

    [Fact]
    public async Task AQuery_ConvertsWhenAVersionIsAsked()
    {
        GivenCardNamed("a.vcf", fn: "Ada", version: "3.0");

        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(EmptyFilter(), version: "4.0"));

        Assert.Contains("VERSION:4.0", AddressDataOf(response).Single());
    }

    [Fact]
    public async Task ACarddavLimit_TruncatesAndSaysSo()
    {
        GivenCards("a.vcf", "b.vcf", "c.vcf");

        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(EmptyFilter(), carddavLimit: 2));

        Assert.Equal(2, ResponsesOfStatus(response, 200).Count);
        Assert.Single(ResponsesOfStatus(response, 507));
        Assert.Single(XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.Dav + "number-of-matches-within-limits"));
    }

    [Fact]
    public async Task ACarddavLimitTheBookDoesNotReach_TruncatesNothing()
    {
        GivenCards("a.vcf", "b.vcf");

        // The bound is a ceiling, not a promise of a 507: a book that fits under it is a complete
        // answer, and a truncation response written anyway would send the client re-reading for ever.
        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(EmptyFilter(), carddavLimit: 2));

        Assert.Equal(2, ResponsesOf(response).Count);
        Assert.Empty(ResponsesOfStatus(response, 507));
    }

    [Fact]
    public async Task ACarddavLimitThatNamesNoReadableNresults_Answers400()
    {
        GivenCards("a.vcf");

        // The second 400: a bound we cannot read is not a bound we may ignore — serving the whole
        // book would hand the client exactly what it wrote the element to prevent.
        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(EmptyFilter(), carddavNresults: "some"));

        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task ADavLimitOnAQuery_IsNotItsBound()
    {
        GivenCards("a.vcf", "b.vcf", "c.vcf");

        var response = await Report(DavPaths.Collection(UserId), QueryBody(EmptyFilter(), davLimit: 1));

        // A reader listening only to DAV: would SILENTLY ignore the bound an addressbook-query
        // client set, and serve it the five thousand cards it had just said it could not digest.
        Assert.Equal(3, ResponsesOfStatus(response, 200).Count);
    }

    [Fact]
    public async Task AnUnevaluableFilter_Answers403SupportedFilter()
    {
        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(Filter(new XElement(DavXml.CardDav + "comp-filter"))));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CardDav + "supported-filter", ConditionOf(response));
    }

    [Fact]
    public async Task AnUnknownCollation_Answers403SupportedCollation()
    {
        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(Filter(PropFilter("FN", TextMatch("x", collation: "i;octet")))));

        Assert.Equal(DavXml.CardDav + "supported-collation", ConditionOf(response));
    }

    [Fact]
    public async Task AQuery_LeavesOutTheCardsTheProtocolCannotSee()
    {
        GivenCards("a.vcf");
        GivenACardWithNoName();

        var response = await Report(DavPaths.Collection(UserId), QueryBody(EmptyFilter()));

        Assert.Single(ResponsesOf(response));
    }

    [Fact]
    public async Task AQueryOnACard_IsServed()
    {
        GivenCards("a.vcf");

        // supported-report-set says so on each card, and a Depth: 0 query on a card is sabre's
        // nominal case for that Depth. The routes must follow, or the header lies.
        var response = await Report(DavPaths.Card(UserId, "a.vcf"), QueryBody(EmptyFilter()));

        Assert.Equal(207, response.StatusCode);
    }

    [Fact]
    public async Task AQueryOnACard_StillHonoursTheFilter()
    {
        GivenCardNamed("a.vcf", fn: "Ada Lovelace");

        var response = await Report(DavPaths.Card(UserId, "a.vcf"),
            QueryBody(Filter(PropFilter("FN", TextMatch("Hopper")))));

        Assert.Equal(207, response.StatusCode);
        Assert.Empty(ResponsesOf(response));
    }

    [Fact]
    public async Task AQueryOnACardTheBookDoesNotHold_Answers404()
    {
        var response = await Report(DavPaths.Card(UserId, "gone.vcf"), QueryBody(EmptyFilter()));

        // An empty multistatus would claim the card exists and matches nothing.
        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task ADepthOfZero_StillReturnsTheMatches_AndThatIsANamedDivergence()
    {
        GivenCards("a.vcf", "b.vcf");

        var response = await Report(DavPaths.Collection(UserId), QueryBody(EmptyFilter()), depth: "0");

        // § 8.6 makes the report's scope its Depth header, so a Depth: 0 should evaluate the
        // collection alone and return no card. We return the filter's result whatever the value: no
        // known client sends Depth: 0 on a request it expects cards from, and returning zero cards
        // to somebody asking for them is precisely the failure mode this whole spec chases.
        // ccs-caldavtester may raise it in 4d — a named divergence, not a discovery.
        Assert.Equal(2, ResponsesOf(response).Count);
    }

    private Task<DavTestResponse> Report(string path, string? body, string? depth = null) =>
        server.SendAsync("REPORT", path, body, depth);

    private void GivenCards(params string[] names)
    {
        foreach (var name in names) GivenCardNamed(name, fn: name);
    }

    /// <param name="displayName">the projected column; defaults to the card's own FN, which is
    /// what the projector writes whenever the FN is not the fallback it would rebuild</param>
    private void GivenCardNamed(string davName, string fn, string? email = null,
        string version = "3.0", string? displayName = "")
    {
        var lines = $"BEGIN:VCARD\r\nVERSION:{version}\r\nUID:u-{davName}\r\nFN:{fn}\r\n";
        if (email is not null) lines += $"EMAIL:{email}\r\n";
        Seed(davName, lines + "END:VCARD\r\n", displayName == "" ? fn : displayName);
    }

    /// <summary>A 4.0 card carrying two FN, stored verbatim, with the column the projector fills
    /// from the first of them.</summary>
    private void GivenTwoFnCard(string davName, string first, string second) =>
        Seed(davName,
            $"BEGIN:VCARD\r\nVERSION:4.0\r\nUID:u-{davName}\r\nFN:{first}\r\nFN:{second}\r\nEND:VCARD\r\n",
            first);

    /// <summary>A row the 4a backfill has not reached: no dav_name, so no href can name it.</summary>
    private void GivenACardWithNoName() =>
        Seed(null, "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:invisible\r\nFN:Invisible\r\nEND:VCARD\r\n", null);

    private void Seed(string? davName, string vCard, string? displayName)
    {
        using var db = server.CreateContext();
        var id = Guid.NewGuid();
        db.Contacts.Add(new Contact
        {
            Id = id,
            UserId = UserId,
            Uid = id.ToString(),
            DavName = davName,
            DisplayName = displayName,
            VCardRaw = vCard,
            CardHash = $"hash-of-{davName ?? id.ToString()}",
            UpdatedAt = DateTime.UtcNow,
            SyncSequence = ++sequence,
        });
        db.SaveChanges();
    }

    private static XElement EmptyFilter() => new(DavXml.CardDav + "filter");

    private static XElement Filter(params XElement[] children) =>
        new(DavXml.CardDav + "filter", children);

    private static XElement PropFilter(string name, params XElement[] children) =>
        new(DavXml.CardDav + "prop-filter", new XAttribute("name", name), children);

    private static XElement TextMatch(string value, string? collation = null) =>
        new(DavXml.CardDav + "text-match",
            collation is null ? null : new XAttribute("collation", collation), value);

    private static string QueryBodyWithoutFilter() =>
        new XDocument(new XElement(DavXml.CardDav + "addressbook-query",
            new XElement(DavXml.Prop, new XElement(DavXml.Dav + "getetag")))).ToString();

    private static string QueryBody(XElement filter, bool withAddressData = false,
        string[]? addressDataProps = null, string? version = null, int? carddavLimit = null,
        string? carddavNresults = null, int? davLimit = null)
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

        var root = new XElement(DavXml.CardDav + "addressbook-query", prop, filter);
        if ((carddavNresults ?? carddavLimit?.ToString()) is { } nresults)
        {
            root.Add(new XElement(DavXml.CardDav + "limit",
                new XElement(DavXml.CardDav + "nresults", nresults)));
        }

        if (davLimit is { } dav)
        {
            root.Add(new XElement(DavXml.Dav + "limit",
                new XElement(DavXml.Dav + "nresults", dav.ToString())));
        }

        return new XDocument(root).ToString();
    }

    private static XName ConditionOf(DavTestResponse response) =>
        XDocument.Parse(response.Body).Root!.Elements().Single().Name;

    private static List<XElement> ResponsesOf(DavTestResponse response) =>
        [.. XDocument.Parse(response.Body).Root!.Elements(DavXml.Response)];

    private static List<XElement> ResponsesOfStatus(DavTestResponse response, int statusCode) =>
        [.. ResponsesOf(response).Where(r => r.Descendants(DavXml.Status)
            .Any(s => s.Value == MultiStatusWriter.StatusLine(statusCode)))];

    private static List<string> HrefsOf(DavTestResponse response) =>
        [.. ResponsesOf(response).Select(r => r.Element(DavXml.Href)!.Value)];

    private static List<string> AddressDataOf(DavTestResponse response) =>
        [.. XDocument.Parse(response.Body)
            .Descendants(DavXml.CardDav + "address-data").Select(e => e.Value)];
}
