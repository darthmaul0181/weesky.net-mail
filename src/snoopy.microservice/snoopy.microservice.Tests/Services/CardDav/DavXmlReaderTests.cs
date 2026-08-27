using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Services.CardDav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CardDav;

public sealed class DavXmlReaderTests
{
    [Fact]
    public void ADtd_IsRefused()
    {
        var body = ToStream(
            "<!DOCTYPE t [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><t>&x;</t>");

        // The classic hole of this protocol: a local file read and echoed back inside a multistatus.
        Assert.Throws<DavBadRequestException>(() => DavXmlReader.Parse(body));
    }

    [Fact]
    public void AnExternalEntity_IsRefused()
    {
        var body = ToStream(
            "<?xml version=\"1.0\"?><!DOCTYPE r SYSTEM \"http://example.invalid/x.dtd\"><r/>");

        Assert.Throws<DavBadRequestException>(() => DavXmlReader.Parse(body));
    }

    [Fact]
    public void ADocumentDeeperThanFifty_IsRefused()
    {
        var body = ToStream(Nested(DavXmlReader.MaxDepth + 5));

        // DtdProcessing closes entity expansion, not the stack. A .NET stack overflow cannot be
        // caught: it takes down the process serving every user.
        Assert.Throws<DavBadRequestException>(() => DavXmlReader.Parse(body));
    }

    [Fact]
    public void ADocumentAtFiftyExactly_IsAccepted() =>
        Assert.NotNull(DavXmlReader.Parse(ToStream(Nested(DavXmlReader.MaxDepth))));

    [Fact]
    public void ADocumentOfFiftyOnePlusOne_IsRefused()
    {
        // MaxDepth means what it reads as: 50 nested elements are accepted (the case above), 51
        // is the first refused — not 52, which >MaxDepth on a 0-based Depth would have let through.
        Assert.Throws<DavBadRequestException>(() =>
            DavXmlReader.Parse(ToStream(Nested(DavXmlReader.MaxDepth + 1))));
    }

    [Fact]
    public void AnEmptyBody_IsNotAnError()
    {
        // Several clients send one on PROPFIND at discovery, and it means allprop (RFC 4918 § 9.1).
        Assert.Null(DavXmlReader.Parse(ToStream("")));
    }

    [Fact]
    public void AWhitespaceOnlyBody_IsAlsoNotAnError() =>
        Assert.Null(DavXmlReader.Parse(ToStream("   \r\n\t  ")));

    [Fact]
    public void AStreamThatYieldsNothingOnFirstRead_IsAlsoNotAnError() =>
        Assert.Null(DavXmlReader.Parse(new ZeroByteFirstReadStream()));

    [Fact]
    public void PlainText_IsRefusedRatherThanEscaping()
    {
        // Not XML at all: the classic "data at the root level is invalid" case must still surface
        // as DavBadRequestException, never as a raw XmlException reaching the caller.
        Assert.Throws<DavBadRequestException>(() => DavXmlReader.Parse(ToStream("hello world")));
    }

    [Fact]
    public void ATruncatedDocument_IsRefused() =>
        Assert.Throws<DavBadRequestException>(() => DavXmlReader.Parse(ToStream("<a><b>")));

    [Fact]
    public void ValidXmlThatIsNotDav_IsStillParsed()
    {
        // Parse only judges well-formedness; whether the document is a recognised DAV request is a
        // later layer's question, not this one's.
        var document = DavXmlReader.Parse(ToStream("<foo/>"));

        Assert.NotNull(document);
        Assert.Equal("foo", document!.Root!.Name.LocalName);
    }

    [Fact]
    public void AStreamThatThrowsWhileBeingRead_IsRefusedRatherThanEscaping() =>
        Assert.Throws<DavBadRequestException>(() => DavXmlReader.Parse(new ThrowingStream(new IOException("connection reset"))));

    [Fact]
    public void ATornDownKestrelBody_IsRefusedRatherThanEscaping()
    {
        // What an aborted request body throws once the connection behind it is gone — measured
        // both ways: "Cannot access a closed Stream" and "Cannot access a disposed object".
        Assert.Throws<DavBadRequestException>(() =>
            DavXmlReader.Parse(new ThrowingStream(new ObjectDisposedException("body"))));
    }

    [Fact]
    public void AGenuineIoFailure_IsLoggedRatherThanLeavingNoTrace()
    {
        var logger = new Mock<ILogger>();

        Assert.Throws<DavBadRequestException>(() =>
            DavXmlReader.Parse(new ThrowingStream(new IOException("disk spill failed")), logger.Object));

        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.Is<IOException>(ex => ex.Message == "disk spill failed"),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public void ABodyOverTheSizeLimit_IsLetThroughRatherThanReportedAsMalformed()
    {
        // BadHttpRequestException derives from IOException — once [RequestSizeLimit] guards the
        // routes, a body over it must still surface as a 413, not be swallowed here as a 400 that
        // hides the real reason.
        var thrown = Assert.Throws<BadHttpRequestException>(() =>
            DavXmlReader.Parse(new ThrowingStream(
                new BadHttpRequestException("Request body too large.", 413))));
        Assert.Equal(413, thrown.StatusCode);
    }

    [Fact]
    public void AUtf16DocumentWithABom_IsStillParsedCorrectly()
    {
        var bytes = new byte[] { 0xFF, 0xFE }
            .Concat(Encoding.Unicode.GetBytes("<foo/>"))
            .ToArray();

        var document = DavXmlReader.Parse(new MemoryStream(bytes));

        Assert.Equal("foo", document!.Root!.Name.LocalName);
    }

    [Fact]
    public void AUtf8BomFollowedByOnlyWhitespace_IsAlsoNotAnError()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("   \n"))
            .ToArray();

        Assert.Null(DavXmlReader.Parse(new MemoryStream(bytes)));
    }

    [Fact]
    public void MalformedXml_IsRefused() =>
        Assert.Throws<DavBadRequestException>(() => DavXmlReader.Parse(ToStream("<a><b></a>")));

    [Theory]
    [InlineData("<D:propfind xmlns:D=\"DAV:\"><D:prop/></D:propfind>")]
    [InlineData("<d:propfind xmlns:d=\"DAV:\"><d:prop/></d:propfind>")]
    [InlineData("<a:propfind xmlns:a=\"DAV:\"><a:prop/></a:propfind>")]
    [InlineData("<propfind xmlns=\"DAV:\"><prop/></propfind>")]
    public void ThePrefixIsIrrelevant_OnlyTheNamespaceAndLocalNameCount(string xml)
    {
        var document = DavXmlReader.Parse(ToStream(xml));

        // Clients write D:, d:, a: or nothing. A reader comparing "D:prop" works against the RFC's
        // example and fails against the first real client.
        Assert.NotNull(document!.Root!.Element(DavXml.Dav + "prop"));
    }

    private static Stream ToStream(string xml) => new MemoryStream(Encoding.UTF8.GetBytes(xml));

    /// <summary>
    /// <paramref name="levels"/> nested elements, the innermost self-closed. No text content: a
    /// text node inside the innermost element sits one Depth past its parent, which would silently
    /// make this produce <paramref name="levels"/> + 1 levels rather than exactly the number named.
    /// </summary>
    private static string Nested(int levels)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < levels - 1; i++) builder.Append($"<e{i}>");
        builder.Append($"<e{levels - 1}/>");
        for (var i = levels - 2; i >= 0; i--) builder.Append($"</e{i}>");
        return builder.ToString();
    }

    private sealed class ZeroByteFirstReadStream : MemoryStream
    {
        private bool firstRead = true;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (firstRead)
            {
                firstRead = false;
                return 0;
            }

            return base.Read(buffer, offset, count);
        }
    }

    private sealed class ThrowingStream(Exception toThrow) : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw toThrow;
    }
}
