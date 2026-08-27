using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// Parses a REPORT/PROPFIND/PROPPATCH body — untrusted input, refused defensively. DTD
/// prohibition and a null resolver close entity expansion, the classic hole where a local file is
/// read and echoed back inside a multistatus. The depth ceiling closes something neither of those
/// touches: the megabyte a request is allowed already leaves room for a great many nested tags,
/// tree construction descends into them, and a .NET stack overflow cannot be caught — it takes
/// down the process serving every user, not just the one request. No legitimate request of this
/// protocol nests past about ten levels.
/// </summary>
internal static class DavXmlReader
{
    internal const int MaxDepth = 50;

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreWhitespace = true,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    /// <summary>
    /// Answers null on an empty body — several clients send one on PROPFIND at discovery, and it
    /// means allprop (RFC 4918 §9.1). Throws <see cref="DavBadRequestException"/> on a DTD, an
    /// entity, malformed XML, excess depth, or a body stream that fails while being read.
    /// </summary>
    internal static XDocument? Parse(Stream body)
    {
        using var buffer = new MemoryStream();
        try
        {
            body.CopyTo(buffer);
        }
        catch (IOException ex)
        {
            throw new DavBadRequestException("The request body could not be read.", ex);
        }

        if (buffer.Length == 0) return null;

        buffer.Position = 0;
        using (var textReader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
                   bufferSize: 1024, leaveOpen: true))
        {
            // Whitespace-only is empty too: several clients pad a PROPFIND with a trailing
            // newline, and none of these three shapes may throw.
            if (string.IsNullOrWhiteSpace(textReader.ReadToEnd())) return null;
        }

        try
        {
            buffer.Position = 0;
            // Depth is counted on a flat read first: XmlReader.Depth is a counter over an
            // iterative state machine, not a recursive descent, so walking it never risks the
            // stack this ceiling exists to protect. Checking it only after XDocument.Load had
            // built the tree would mean checking after the very descent being guarded against —
            // no ceiling at all.
            using (var probe = XmlReader.Create(buffer, Settings))
            {
                while (probe.Read())
                {
                    if (probe.Depth > MaxDepth)
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
}
