using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CardDav;

/// <summary>
/// The one line every /dav request leaves. The symptom of nearly every failure of this protocol is
/// the same — "the book is empty on the client" — and the causes are five: a swallowed
/// Authorization header, a PROPFIND a firewall refused, an incomplete backfill, a token refused in
/// a loop, a report we do not serve. What this line carries is exactly what separates them.
/// </summary>
public sealed class DavRequestLogTests
{
    private static readonly Guid UserId = Guid.Parse("2f1c9a34-6d0b-4a1e-9c77-5b0e3d8a4f21");

    [Fact]
    public void TheLine_CarriesWhatSeparatesTheFiveCauses()
    {
        var logger = new Mock<ILogger<CardDavController>>();

        DavRequestLog.Write(logger.Object, new DavRequestTrace(
            Method: "REPORT", Resource: "/dav/addressbooks/x/default/", Depth: null,
            Report: "addressbook-multiget", TokenIn: null, TokenOut: null,
            Responses: 42, StatusCode: 207, Condition: null));

        // Five causes, one symptom — "the book is empty" — and no server-side trace to tell them
        // apart. This turns 4d's conformance work into log reading rather than packet capture.
        logger.VerifyInformationLoggedWithAll("REPORT", "addressbook-multiget", "42", "207");
    }

    [Fact]
    public void TheLine_NamesThePreconditionWhenThereIsOne()
    {
        var logger = new Mock<ILogger<CardDavController>>();

        DavRequestLog.Write(logger.Object, new DavRequestTrace(
            Method: "PROPFIND", Resource: "/dav/addressbooks/x/default/", Depth: null,
            Report: null, TokenIn: null, TokenOut: null,
            Responses: 0, StatusCode: 403, Condition: "propfind-finite-depth"));

        logger.VerifyInformationLoggedWithAll("propfind-finite-depth");
    }

    [Fact]
    public void TheLine_NeverCarriesAnIdentifierNorACard()
    {
        var logger = new Mock<ILogger<CardDavController>>();

        DavRequestLog.Write(logger.Object, new DavRequestTrace(
            Method: "GET", Resource: DavPaths.Card(UserId, "a.vcf"), Depth: null,
            Report: null, TokenIn: null, TokenOut: null,
            Responses: 1, StatusCode: 200, Condition: null));

        // The user in a log line is the principal's GUID — the one already in the URL. Never the
        // address, never the secret, never a card's content.
        logger.VerifyNoLoggedValueContains("@");
        logger.VerifyNoLoggedValueContains("BEGIN:VCARD");
    }

    [Fact]
    public void TheLine_IsOneMessageTemplateSoItCanBeFiltered()
    {
        var logger = new Mock<ILogger<CardDavController>>();

        DavRequestLog.Write(logger.Object, new DavRequestTrace(
            Method: "PROPFIND", Resource: "/dav/", Depth: "0", Report: null,
            TokenIn: null, TokenOut: null, Responses: 1, StatusCode: 207, Condition: null));

        // Structured, never interpolated: the template is what a log query filters on, and an
        // interpolated line leaves a different string on every request.
        Assert.StartsWith("dav {Method} ", logger.SingleTemplate());
    }

    [Fact]
    public void TheLine_BoundsWhatTheClientChoseTheNameOf()
    {
        var logger = new Mock<ILogger<CardDavController>>();

        DavRequestLog.Write(logger.Object, new DavRequestTrace(
            Method: "REPORT", Resource: "/dav/", Depth: null, Report: new string('r', 500),
            TokenIn: null, TokenOut: null, Responses: 0, StatusCode: 403,
            Condition: new string('c', 500)));

        // A report's name comes off the client's own root element: unbounded there is a log the
        // client can flood one request at a time.
        logger.VerifyNoLoggedValueContains(new string('r', DavRequestLog.MaxFieldLength + 1));
        logger.VerifyNoLoggedValueContains(new string('c', DavRequestLog.MaxFieldLength + 1));
    }
}
