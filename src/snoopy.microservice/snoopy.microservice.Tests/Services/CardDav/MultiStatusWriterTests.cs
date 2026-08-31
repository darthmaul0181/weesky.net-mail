using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using weesky.Snoopy.Microservice.Services.CardDav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CardDav;

public sealed class MultiStatusWriterTests
{
    [Fact]
    public async Task ItAnswers207WithTypedXmlAndTheDavHeader()
    {
        var context = NewContext();

        await using (await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None)) { }

        Assert.Equal(207, context.Response.StatusCode);
        Assert.Equal("application/xml; charset=utf-8", context.Response.ContentType);
        // On PROPFIND too, not only OPTIONS: sabre does it deliberately and Apple clients depend on it.
        Assert.Equal(DavHeaders.ComplianceClasses, context.Response.Headers["DAV"].ToString());
    }

    [Fact]
    public async Task TheFoundPropstat_IsWrittenBeforeTheMissingOne()
    {
        var context = NewContext();

        await using (var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None))
        {
            await writer.WriteResourceAsync("/dav/x/",
                [new XElement(DavXml.Dav + "displayname", "Book")],
                [DavXml.CardDav + "addressbook-description"],
                CancellationToken.None);
        }

        // Thunderbird reads the FIRST descendant status of a response and compares it to the string
        // "HTTP/1.1 200 OK". Writing the 404 propstat first makes every response read as a failure.
        var body = ReadBody(context.Response);
        Assert.True(body.IndexOf("200 OK", StringComparison.Ordinal)
                    < body.IndexOf("404 Not Found", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(200, "HTTP/1.1 200 OK")]
    [InlineData(404, "HTTP/1.1 404 Not Found")]
    [InlineData(403, "HTTP/1.1 403 Forbidden")]
    [InlineData(507, "HTTP/1.1 507 Insufficient Storage")]
    public async Task TheStatusLine_IsLiteral(int code, string expected)
    {
        var context = NewContext();

        await using (var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None))
            await writer.WriteStatusAsync("/dav/x/a.vcf", code, CancellationToken.None);

        // sabre has already had to correct an "Ok" for iOS. These strings are compared byte for byte.
        Assert.Contains(expected, ReadBody(context.Response));
    }

    [Fact]
    public async Task AStatusResponse_CarriesItsStatusAsADirectChild()
    {
        var context = NewContext();

        await using (var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None))
            await writer.WriteStatusAsync("/dav/x/gone.vcf", 404, CancellationToken.None);

        // The shape a sync-collection tombstone takes: never lodged inside a propstat. Written here
        // rather than in plan c so the literality lives in one place.
        var response = XDocument.Parse(ReadBody(context.Response)).Root!.Elements(DavXml.Dav + "response").Single();
        Assert.Single(response.Elements(DavXml.Dav + "status"));
        Assert.Empty(response.Elements(DavXml.Dav + "propstat"));
    }

    [Fact]
    public async Task AResourceWithNothingMissing_CarriesOneSinglePropstat()
    {
        var context = NewContext();

        await using (var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None))
        {
            await writer.WriteResourceAsync("/dav/x/",
                [new XElement(DavXml.Dav + "displayname", "Book")], [], CancellationToken.None);
        }

        var response = XDocument.Parse(ReadBody(context.Response)).Root!.Elements(DavXml.Dav + "response").Single();
        Assert.Single(response.Elements(DavXml.Dav + "propstat"));
    }

    [Fact]
    public async Task AResourceWithNeitherPropstat_FallsBackOnADirectStatus()
    {
        var context = NewContext();

        await using (var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None))
            await writer.WriteResourceAsync("/dav/x/a.vcf", [], [], CancellationToken.None);

        // RFC 4918 § 14.24 admits (href, status) or (href, propstat+) and nothing else — an href
        // alone is what an empty prop used to produce, and no conforming client can read it.
        var response = XDocument.Parse(ReadBody(context.Response)).Root!
            .Elements(DavXml.Dav + "response").Single();
        Assert.Equal("HTTP/1.1 200 OK", response.Elements(DavXml.Dav + "status").Single().Value);
        Assert.Empty(response.Elements(DavXml.Dav + "propstat"));
    }

    [Fact]
    public async Task AMissingProperty_IsNamedWithoutAValue()
    {
        var context = NewContext();

        await using (var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None))
        {
            await writer.WriteResourceAsync("/dav/x/", [],
                [DavXml.Dav + "acl"], CancellationToken.None);
        }

        // Pure omission is what makes a client wait for ever for a value it believes is on its way.
        var propstat = XDocument.Parse(ReadBody(context.Response))
            .Descendants(DavXml.Dav + "propstat").Single();
        Assert.Contains("404 Not Found", propstat.Element(DavXml.Dav + "status")!.Value);
        Assert.Single(propstat.Element(DavXml.Dav + "prop")!.Elements(DavXml.Dav + "acl"));
    }

    [Fact]
    public async Task AnHref_IsWrittenEscapedAsItWasGiven()
    {
        var context = NewContext();
        var href = DavPaths.Card(Guid.NewGuid(), "un nom.vcf");

        await using (var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None))
            await writer.WriteStatusAsync(href, 404, CancellationToken.None);

        // The writer never re-escapes and never unescapes: DavPaths owns both directions, and doing
        // it twice would give a client an href it cannot read back.
        Assert.Equal(href, XDocument.Parse(ReadBody(context.Response))
            .Descendants(DavXml.Dav + "href").Single().Value);
    }

    [Fact]
    public async Task TheDocument_StreamsRatherThanBuffering()
    {
        var context = NewContext();

        await using var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None);
        await writer.WriteResourceAsync("/dav/x/a.vcf",
            [new XElement(DavXml.Dav + "getetag", "\"h1\"")], [], CancellationToken.None);
        await writer.FlushAsync(CancellationToken.None);

        // A full book with address-data runs to gigabytes: the first response must be on the wire
        // before the last one is composed, or the whole book sits in the heap of a process serving
        // every user.
        Assert.Contains("a.vcf", ReadBody(context.Response));
    }

    [Fact]
    public async Task FlushIfDue_PushesOnceEveryFlushEveryResponses_AndNotBefore()
    {
        var context = NewContext();

        await using var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None);
        for (var i = 0; i < MultiStatusWriter.FlushEvery - 1; i++)
        {
            await writer.WriteStatusAsync($"/dav/x/default/{i}.vcf", 404, CancellationToken.None);
            await writer.FlushIfDueAsync(CancellationToken.None);
        }

        // One short of the batch: nothing of this document may have started, because a flush is
        // what makes HasStarted true and takes the whole response away from a refusal.
        Assert.DoesNotContain("0.vcf", ReadBody(context.Response));

        await writer.WriteStatusAsync("/dav/x/default/last.vcf", 404, CancellationToken.None);
        await writer.FlushIfDueAsync(CancellationToken.None);

        // The batch closed: without this the streaming writer holds everything to disposal, which
        // is the one thing its design refuses — and the promise no caller kept before.
        Assert.Contains("0.vcf", ReadBody(context.Response));
        Assert.Contains("last.vcf", ReadBody(context.Response));
    }

    [Fact]
    public async Task FlushIfDue_CountsFromTheLastPush_NotFromTheStart()
    {
        var context = NewContext();

        await using var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None);
        for (var i = 0; i < MultiStatusWriter.FlushEvery; i++)
            await writer.WriteStatusAsync($"/dav/x/default/{i}.vcf", 404, CancellationToken.None);
        await writer.FlushIfDueAsync(CancellationToken.None);

        var afterFirstBatch = ReadBody(context.Response).Length;
        await writer.WriteStatusAsync("/dav/x/default/next.vcf", 404, CancellationToken.None);
        await writer.FlushIfDueAsync(CancellationToken.None);

        // Counted against the count at the last push, so a long answer pushes every batch rather
        // than every response once the first threshold is passed — the difference between a flush
        // per 64 rows and a syscall per row on a 5000-card book.
        Assert.Equal(afterFirstBatch, ReadBody(context.Response).Length);
    }

    [Fact]
    public async Task TheTruncationShape_IsTheOneRfc6352Names()
    {
        var context = NewContext();

        await using (var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None))
            await writer.WriteTruncatedAsync("/dav/x/default/", null, CancellationToken.None);

        // Not a bare 403, which rests on no text: § 8.6.2's shape is what clients already read.
        var response = XDocument.Parse(ReadBody(context.Response)).Root!.Elements(DavXml.Dav + "response").Single();
        Assert.Contains("507 Insufficient Storage", response.Element(DavXml.Dav + "status")!.Value);
        Assert.Single(response.Descendants(DavXml.Dav + "number-of-matches-within-limits"));
    }

    private static DefaultHttpContext NewContext() => new() { Response = { Body = new MemoryStream() } };

    // ToArray() reads the buffer without disturbing Position, unlike a StreamReader over the same
    // stream (which would dispose it): several of these tests read the body while the writer is
    // still open and about to write more — closing tags on disposal, or more responses mid-test.
    private static string ReadBody(HttpResponse response) =>
        Encoding.UTF8.GetString(((MemoryStream)response.Body).ToArray());
}
