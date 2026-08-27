using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Moq;
using weesky.Snoopy.Microservice.Services.CardDav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CardDav;

public sealed class DavErrorTests
{
    [Fact]
    public async Task AnErrorBody_HasErrorAsItsRootAndIsTypedXml()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await DavError.WriteAsync(context.Response, 403, DavXml.CardDav + "supported-report");

        // DAVx5 extracts a precondition only from an XML-typed body whose root is `error`. A 403
        // served as text/plain makes it fail on every cycle instead of starting over.
        Assert.Equal(403, context.Response.StatusCode);
        Assert.Equal("application/xml; charset=utf-8", context.Response.ContentType);
        var written = ReadBody(context.Response);
        Assert.StartsWith("<?xml", written);
        Assert.Equal(DavXml.Dav + "error", XDocument.Parse(written).Root!.Name);
    }

    [Fact]
    public async Task AnErrorBody_NamesItsConditionInsideTheRoot()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await DavError.WriteAsync(context.Response, 403, DavXml.Dav + "propfind-finite-depth");

        var root = XDocument.Parse(ReadBody(context.Response)).Root!;
        // A bare 403 leaves the client nothing but giving up; the condition is what it reads to
        // choose its fallback.
        Assert.Single(root.Elements(DavXml.Dav + "propfind-finite-depth"));
    }

    [Fact]
    public async Task AConditionMayCarryDetail()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var detail = new XElement(DavXml.Dav + "href", "/dav/addressbooks/x/default/a.vcf");

        await DavError.WriteAsync(context.Response, 403, DavXml.CardDav + "no-uid-conflict", detail);

        // no-uid-conflict carries the href of the conflicting resource: without it the client knows
        // it lost but not to whom.
        var condition = XDocument.Parse(ReadBody(context.Response)).Root!
            .Element(DavXml.CardDav + "no-uid-conflict")!;
        Assert.Equal("/dav/addressbooks/x/default/a.vcf", condition.Element(DavXml.Dav + "href")!.Value);
    }

    [Fact]
    public async Task TheStatusCodeIsWhateverTheCallerNames() =>
        await AssertStatusAsync(207);

    private static async Task AssertStatusAsync(int statusCode)
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await DavError.WriteAsync(context.Response, statusCode, DavXml.Dav + "lock-token-matches-request-uri");

        Assert.Equal(statusCode, context.Response.StatusCode);
    }

    [Fact]
    public async Task AResponseThatHasAlreadyStarted_IsAQuietNoOpRatherThanAThrow()
    {
        // DefaultHttpContext's own HasStarted never flips true from a bare MemoryStream body — it
        // is Kestrel's writer that sets it in practice — so the started state is faked at the
        // feature level, which is the only thing HttpResponse.HasStarted actually reads.
        var responseFeature = new Mock<IHttpResponseFeature>();
        responseFeature.SetupGet(f => f.HasStarted).Returns(true);
        var features = new FeatureCollection();
        features.Set(responseFeature.Object);
        var context = new DefaultHttpContext(features);

        // A caller here is a writer already mid-multistatus, not a bug: turning a truncated
        // document into an unhandled exception on top of it would be the very 500 this type
        // exists to avoid. Nothing else may be attempted on a response that can no longer carry it.
        await DavError.WriteAsync(context.Response, 403, DavXml.Dav + "error");

        responseFeature.VerifySet(f => f.StatusCode = It.IsAny<int>(), Times.Never);
    }

    [Fact]
    public async Task ACancelledToken_ThrowsRatherThanWritingAnything()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            DavError.WriteAsync(context.Response, 403, DavXml.Dav + "error", cancellationToken: cts.Token));
    }

    private static string ReadBody(HttpResponse response)
    {
        response.Body.Position = 0;
        using var reader = new StreamReader(response.Body);
        return reader.ReadToEnd();
    }
}
