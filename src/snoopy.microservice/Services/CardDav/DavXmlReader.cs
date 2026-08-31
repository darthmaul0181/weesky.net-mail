using System.Xml;
using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// Parses a REPORT/PROPFIND/PROPPATCH body — untrusted input, refused defensively. DTD
/// prohibition and a null resolver close entity expansion, the classic hole where a local file is
/// read and echoed back inside a multistatus.
/// </summary>
/// <remarks>
/// The depth ceiling does <c>not</c> protect this method. <see cref="XmlReader.Read"/> advances an
/// iterative state machine — its element stack lives on the heap, not on the call stack — and
/// <see cref="XDocument.Load(XmlReader)"/>'s own tree construction is iterative too
/// (<c>XContainer.ReadContentFrom</c> walks with <c>c = e</c> / <c>c = c.parent</c>, never
/// recursion): a document 200000 levels deep loads here without incident. <b>Do not remove this
/// ceiling on the reasoning that <see cref="XDocument"/> does not recurse</b> — that reasoning is
/// correct about this file and wrong about what this file feeds. The document <see cref="Parse"/>
/// returns is serialised back out downstream through <c>XNode.WriteTo</c>/<c>ToString</c>
/// (<c>MultiStatusWriter</c> and the report/error writers of the routes to come), and <em>that</em>
/// recurses one call-stack frame per level, uncatchably, on the process serving every other user's
/// request at the same time. Refusing past <see cref="MaxDepth"/> here — before any writer ever
/// sees the document — is the one place all of those write paths funnel through.
/// </remarks>
internal static class DavXmlReader
{
    /// <summary>
    /// No legitimate request of this protocol nests past about ten levels. A document of exactly
    /// this many nested elements is accepted; one more is refused.
    /// </summary>
    internal const int MaxDepth = 50;

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreWhitespace = true,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LeBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BeBom = [0xFE, 0xFF];

    /// <summary>
    /// Answers null on an empty body — several clients send one on PROPFIND at discovery, and it
    /// means allprop (RFC 4918 §9.1). Throws <see cref="DavBadRequestException"/> on a DTD, an
    /// entity, malformed XML, excess depth, or a body stream that fails while being read. The read
    /// is asynchronous ON PURPOSE and this is the only entry point: Kestrel forbids synchronous
    /// reads on a request body, so a synchronous sibling taking the raw body would be a 500 on the
    /// first real request — buffering here, once, is what keeps that duty off every caller.
    /// </summary>
    /// <param name="body">The request body, read exactly once, in full.</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <param name="logger">
    /// Optional: a genuine I/O failure reading the body (as opposed to the body simply being
    /// malformed) is still answered as a 400 to the caller — ruling 2 forbids a 500 here too — but
    /// it is a server-side symptom worth a trace, which converting it silently would erase.
    /// </param>
    internal static async Task<XDocument?> ParseAsync(
        Stream body, CancellationToken cancellationToken, ILogger? logger = null)
    {
        using var buffer = new MemoryStream();
        try
        {
            await body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (BadHttpRequestException)
        {
            // Thrown by [RequestSizeLimit]: the body is too large, not malformed. Reporting that
            // as DavBadRequestException would trade a 413 the client can act on for a 400 that
            // hides the real reason.
            throw;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // What an aborted Kestrel request body gives once the connection behind it has been
            // torn down — a client fact, not a document fact, but still one worth a trace since it
            // may equally be this process's own buffered-body storage failing to spill to disk.
            logger?.LogWarning(ex, "The CardDAV request body stream failed while being read");
            throw new DavBadRequestException("The request body could not be read.", ex);
        }

        return Parse(buffer);
    }

    /// <summary>The synchronous core, safe because it only ever runs on the buffer above.</summary>
    private static XDocument? Parse(MemoryStream buffer)
    {
        if (buffer.Length == 0 || IsWhitespaceOnly(buffer.GetBuffer(), (int)buffer.Length)) return null;

        try
        {
            buffer.Position = 0;
            // Depth is still checked here, on the flat non-recursive reader, even though reading
            // itself was never the risk (see the remarks above): refusing here is what keeps an
            // oversized document from ever reaching the recursive writers downstream.
            using (var probe = XmlReader.Create(buffer, Settings))
            {
                while (probe.Read())
                {
                    // Depth is 0-based (a document's root sits at Depth 0), so a document of
                    // exactly MaxDepth nested elements reaches Depth MaxDepth-1 and must be
                    // accepted; >= is what makes the constant mean what it reads as, rather than
                    // silently admitting one level more than its name promises.
                    if (probe.Depth >= MaxDepth)
                        throw new DavBadRequestException(
                            $"The document nests past the {MaxDepth}-level ceiling.");
                }
            }

            buffer.Position = 0;
            using var xmlReader = XmlReader.Create(buffer, Settings);
            return XDocument.Load(xmlReader);
        }
        catch (XmlException ex)
        {
            throw new DavBadRequestException("The request body is not well-formed XML.", ex);
        }
    }

    /// <summary>
    /// A byte-level check, not a decode: materialising the body as a second, UTF-16 copy just to
    /// answer "is it blank" doubles the memory a request holds for a question about its first few
    /// bytes. Scanning the raw bytes for anything outside the ASCII whitespace set is exact for
    /// UTF-8 and ASCII, and correctly conservative for every other encoding — it may call a
    /// whitespace-only UTF-16 body "not blank" (each unit's zero high byte reads as non-whitespace)
    /// and fall through to the real parse, which still answers correctly via <see cref="XmlException"/>;
    /// it never calls actual content blank, since markup characters always fall outside this set.
    /// </summary>
    private static bool IsWhitespaceOnly(byte[] bytes, int length)
    {
        var start = SkipBom(bytes, length);
        for (var i = start; i < length; i++)
        {
            if (bytes[i] is not (0x09 or 0x0A or 0x0D or 0x20)) return false;
        }

        return true;
    }

    private static int SkipBom(byte[] bytes, int length)
    {
        if (length >= 3 && bytes.AsSpan(0, 3).SequenceEqual(Utf8Bom)) return 3;
        if (length >= 2 && bytes.AsSpan(0, 2).SequenceEqual(Utf16LeBom)) return 2;
        if (length >= 2 && bytes.AsSpan(0, 2).SequenceEqual(Utf16BeBom)) return 2;
        return 0;
    }
}
