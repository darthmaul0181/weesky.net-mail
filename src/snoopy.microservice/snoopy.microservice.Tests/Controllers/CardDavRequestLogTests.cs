using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// Every action of the /dav surface leaves one line, error paths included — those most of all. The
/// symptom of nearly every failure of this protocol is the same one, "the book is empty on the
/// client", and without this line the five causes behind it are separable only by packet capture.
/// </summary>
public sealed class CardDavRequestLogTests : IAsyncLifetime
{
    private readonly Mock<ILogger<CardDavController>> logger = new();

    private DavTestServer server = null!;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync() =>
        server = await DavTestServer.StartAsync(overrides: services =>
            services.AddSingleton(logger.Object));

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Theory]
    [InlineData("OPTIONS")]
    [InlineData("PROPFIND")]
    [InlineData("REPORT")]
    [InlineData("PROPPATCH")]
    [InlineData("MKCOL")]
    [InlineData("GET")]
    public async Task EveryAction_LeavesItsLine(string method)
    {
        await server.SendAsync(method, DavPaths.Collection(UserId), BodyFor(method));

        logger.VerifyInformationLoggedWithAll(method, DavPaths.Collection(UserId));
    }

    [Fact]
    public async Task TheLineIsThereOnAnErrorPath_WithTheConditionThatCausedIt()
    {
        await server.PropfindAsync(DavPaths.Collection(UserId), "infinity", null);

        // The error path is the one that most needs the line: a client looping on an infinite
        // PROPFIND is invisible in an access log, which sees only a 403 it cannot explain.
        logger.VerifyInformationLoggedWithAll("PROPFIND", "status=403", "propfind-finite-depth");
    }

    [Fact]
    public async Task AMalformedBody_LeavesALineCarryingIts400()
    {
        await server.SendAsync("REPORT", DavPaths.Collection(UserId), "<not-closed");

        logger.VerifyInformationLoggedWithAll("REPORT", "status=400");
    }

    [Fact]
    public async Task A404_LeavesALineToo()
    {
        await server.SendAsync("GET", DavPaths.Card(UserId, "missing.vcf"));

        logger.VerifyInformationLoggedWithAll("GET", "status=404");
    }

    [Theory]
    [InlineData("PROPFIND")]
    [InlineData("PROPPATCH")]
    [InlineData("REPORT")]
    public async Task ABodyPastTheLimit_LeavesALineCarryingIts413(string method)
    {
        // Depth 0 on purpose: a PROPFIND with no Depth header is infinity, refused with a 403
        // before the body is ever read, and the size limit would never be the thing that answered.
        var response = await server.SendAsync(method, DavPaths.Collection(UserId), OversizedBody(),
            depth: "0");

        // Kestrel writes this 413 AFTER the action has returned, so a line reading
        // Response.StatusCode in its finally reports the untouched 200 — and an operator
        // diagnosing "the book is empty" for a client sending oversized bodies reads a success.
        Assert.Equal(413, response.StatusCode);
        logger.VerifyInformationLoggedWithAll(method, "status=413");
    }

    [Fact]
    public async Task TheLine_NamesTheReportTheClientAsked()
    {
        await server.SendAsync("REPORT", DavPaths.Collection(UserId), MultigetBody());

        logger.VerifyInformationLoggedWithAll("addressbook-multiget", "status=207");
    }

    [Fact]
    public async Task TheLine_CountsTheResponsesTheBookAnswered()
    {
        GivenCards("a.vcf", "b.vcf");

        await server.PropfindAsync(DavPaths.Collection(UserId), "1", null);

        // "The book is empty on the client" is a claim about this number, and it is the one field
        // an access log can never carry.
        logger.VerifyInformationLoggedWithAll("PROPFIND", "responses=3");
    }

    [Fact]
    public async Task TheLine_CarriesTheDepthTheClientSent()
    {
        await server.PropfindAsync(DavPaths.Collection(UserId), "0", null);

        logger.VerifyInformationLoggedWithAll("depth=0");
    }

    [Fact]
    public async Task NoLine_EverCarriesTheAddressOrACard()
    {
        GivenCards("a.vcf");

        await server.PropfindAsync(DavPaths.Collection(UserId), "1", null);
        await server.SendAsync("GET", DavPaths.Card(UserId, "a.vcf"));
        await server.SendAsync("PROPPATCH", DavPaths.Collection(UserId), ProppatchBody());

        // The authenticated user of this server is someone@weesky.be and the cards it serves are
        // vCards: the user in a line is the principal's GUID, already in the URL, and nothing else.
        logger.VerifyNoLoggedValueContains("@");
        logger.VerifyNoLoggedValueContains("BEGIN:VCARD");
    }

    private static string? BodyFor(string method) => method switch
    {
        "REPORT" => MultigetBody(),
        "PROPPATCH" => ProppatchBody(),
        _ => null,
    };

    /// <summary>Just past the 1 MB the routes announce; well-formed, so only the size refuses it.</summary>
    private static string OversizedBody() =>
        "<oversized>" + new string('a', 1024 * 1024) + "</oversized>";

    private static string MultigetBody() =>
        new XDocument(new XElement(DavXml.CardDav + "addressbook-multiget",
            new XElement(DavXml.Prop, new XElement(DavXml.Dav + "getetag")))).ToString();

    private static string ProppatchBody() =>
        new XDocument(new XElement(DavXml.Dav + "propertyupdate",
            new XElement(DavXml.Dav + "set", new XElement(DavXml.Prop,
                new XElement(DavXml.Dav + "displayname", "X"))))).ToString();

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
}
