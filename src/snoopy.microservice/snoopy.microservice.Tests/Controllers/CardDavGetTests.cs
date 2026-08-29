using System.Text;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class CardDavGetTests : IAsyncLifetime
{
    /// <summary>Derivable from neither the name nor the bytes, so a test claiming to pin the ETag
    /// cannot be satisfied by a value the card happens to carry anyway.</summary>
    private const string DefaultHash = "9f1c2d";

    private DavTestServer server = null!;
    private ulong sequence;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync() => server = await DavTestServer.StartAsync();

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task ItServesTheStoredBytesUntouched()
    {
        const string card = "BEGIN:VCARD\nVERSION:3.0\nUID:u1\nFN:Adá\nEND:VCARD\n";
        GivenCard("a.vcf", card);

        var response = await Get(DavPaths.Card(UserId, "a.vcf"));

        // Line endings included: RFC 6350 wants CRLF, a client sending bare LF produces a
        // non-conforming card, and normalising it would be a TRANSFORMATION — hence a response with
        // no ETag, a re-read, and a card that never coincides with the client's. The server's job is
        // to hand any other client exactly what it received. Compared as BYTES so the accented
        // character also pins UTF-8, and the leading byte pins the absence of a BOM.
        Assert.Equal(Encoding.UTF8.GetBytes(card), response.BodyBytes);
        Assert.Equal(card, await response.ReadAsync());
    }

    [Fact]
    public async Task ItAnswersTheThreeHeadersAClientReads()
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nEND:VCARD\r\n", hash: "abc123",
            updatedAt: new DateTime(2026, 8, 24, 13, 5, 0, DateTimeKind.Utc));

        var response = await Get(DavPaths.Card(UserId, "a.vcf"));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("\"abc123\"", response.Header("ETag"));
        Assert.Equal("text/vcard; charset=utf-8", response.Header("Content-Type"));
        // The same source as getlastmodified, so the two never disagree.
        Assert.Equal("Mon, 24 Aug 2026 13:05:00 GMT", response.Header("Last-Modified"));
    }

    [Theory]
    [InlineData("\"abc123\"")]
    [InlineData("*")]
    [InlineData("W/\"abc123\"")]
    [InlineData("\"other\", \"abc123\"")]
    public async Task AConditionalGetCoveringTheCurrentEtag_Answers304(string ifNoneMatch)
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nEND:VCARD\r\n", hash: "abc123");

        var response = await Get(DavPaths.Card(UserId, "a.vcf"), ifNoneMatch: ifNoneMatch);

        // The full semantics the 4a residual asks for: a list of values, `*`, and weak tags compared
        // weakly on a read.
        Assert.Equal(304, response.StatusCode);
    }

    [Fact]
    public async Task A304_CarriesItsEtagAndNoBody()
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nEND:VCARD\r\n", hash: "abc123");

        var response = await Get(DavPaths.Card(UserId, "a.vcf"), ifNoneMatch: "\"abc123\"");

        Assert.Equal("\"abc123\"", response.Header("ETag"));
        Assert.Empty(await response.ReadAsync());
    }

    [Fact]
    public async Task AConditionalGetThatDoesNotCover_Answers200()
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nEND:VCARD\r\n", hash: "abc123");

        var response = await Get(DavPaths.Card(UserId, "a.vcf"), ifNoneMatch: "\"stale\"");

        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownName_Answers404()
    {
        var response = await Get(DavPaths.Card(UserId, "never-existed.vcf"));

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task ACardTheProtocolCannotSee_Answers404Too()
    {
        GivenACardWithAnEmptyHash("b.vcf");

        // Invisible and absent are the same 404 to a client: serving an empty body with an ETag of
        // "" would be filed like any other value, for ever.
        Assert.Equal(404, (await Get(DavPaths.Card(UserId, "b.vcf"))).StatusCode);
    }

    [Fact]
    public async Task AnotherUsersCard_Answers404()
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nEND:VCARD\r\n");

        Assert.Equal(404, (await Get(DavPaths.Card(Guid.NewGuid(), "a.vcf"))).StatusCode);
    }

    [Fact]
    public async Task ANameTheProtocolRefuses_IsNotServedEvenWhenItIsStored()
    {
        // Stored despite being unspellable: utf8mb4_bin is PAD SPACE, so this row and "a.vcf"
        // collide on the unique index while being two distinct URLs. DavName.IsValid is the only
        // judge, and without it the lookup finds this row and serves it under the other's URL.
        GivenCard("a.vcf ", "BEGIN:VCARD\r\nFN:Unspellable\r\nEND:VCARD\r\n");

        Assert.Equal(404, (await Get($"{DavPaths.Collection(UserId)}a.vcf%20")).StatusCode);
    }

    [Fact]
    public async Task ATraversalInTheName_Answers404RatherThan400()
    {
        // Routing decodes, so "%2F" arrives as the '/' DavName.IsValid refuses. An invalid name
        // designates nothing, which is already the answer an unknown name gets — never a 400, and
        // never an exception escaping as a 500 a client would retry for ever.
        var response = await Get($"{DavPaths.Collection(UserId)}..%2F..%2Fetc");

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task AGetOnTheCollection_Answers405AndNot404()
    {
        var response = await Get(DavPaths.Collection(UserId));

        // Generic WebDAV clients try it. A 500 there makes them abandon the whole book; a 405 is an
        // answer every client knows how to file. The routes only bind GET on {name}, a segment the
        // collection URL does not present — so routing would otherwise give an accidental 404.
        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.CollectionAllow, response.Header("Allow"));

        // HEAD is bound alongside it: a routing 405 carries no Allow, and a client reading the
        // refusal without the verb list learns nothing it can act on.
        var head = await Head(DavPaths.Collection(UserId));
        Assert.Equal(405, head.StatusCode);
        Assert.Equal(DavHeaders.CollectionAllow, head.Header("Allow"));
    }

    [Fact]
    public async Task AHead_CarriesTheSameHeadersAndNoBody()
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nEND:VCARD\r\n", hash: "abc123");

        var response = await Head(DavPaths.Card(UserId, "a.vcf"));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("\"abc123\"", response.Header("ETag"));
        Assert.Empty(await response.ReadAsync());
    }

    [Fact]
    public async Task AnAwkwardNameIsFoundThroughItsEscapedUrl()
    {
        const string awkward = "BEGIN:VCARD\r\nFN:Awkward\r\nEND:VCARD\r\n";
        GivenCard("plain.vcf", "BEGIN:VCARD\r\nFN:Plain\r\nEND:VCARD\r\n");
        GivenCard("un nom#?.vcf", awkward);

        var response = await Get(DavPaths.Card(UserId, "un nom#?.vcf"));

        // The round trip DavPaths owns: the href we write is the URL that comes back. The body is
        // asserted, not merely the status — an href truncated at the '#' would land on the
        // collection, and one truncated at the '?' on another resource entirely.
        Assert.Equal(200, response.StatusCode);
        Assert.Equal(awkward, await response.ReadAsync());
    }

    private Task<DavTestResponse> Get(string path, string? ifNoneMatch = null) =>
        server.SendAsync("GET", path,
            headers: ifNoneMatch is null ? null : new Dictionary<string, string> { ["If-None-Match"] = ifNoneMatch });

    private Task<DavTestResponse> Head(string path) => server.SendAsync("HEAD", path);

    private void GivenCard(string davName, string vCard, string hash = DefaultHash,
        DateTime? updatedAt = null) =>
        Seed(davName, vCard, hash, updatedAt);

    /// <summary>Visible in the webmail, invisible to the protocol: the 4a backfill has not reached
    /// it, so it has no hash and therefore no honest ETag to serve.</summary>
    private void GivenACardWithAnEmptyHash(string davName) =>
        Seed(davName, "BEGIN:VCARD\r\nEND:VCARD\r\n", hash: "", updatedAt: null);

    private void Seed(string davName, string vCard, string hash, DateTime? updatedAt)
    {
        using var db = server.CreateContext();
        var id = Guid.NewGuid();
        db.Contacts.Add(new Contact
        {
            Id = id,
            UserId = UserId,
            Uid = id.ToString(),
            DavName = davName,
            VCardRaw = vCard,
            CardHash = hash,
            UpdatedAt = updatedAt ?? new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            SyncSequence = ++sequence,
        });
        db.SaveChanges();
    }
}
