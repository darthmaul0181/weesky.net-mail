using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// Writes a DAV error body: <c>&lt;D:error xmlns:D="DAV:"&gt;&lt;D:{condition}/&gt;&lt;/D:error&gt;</c>,
/// with the XML declaration, as <c>application/xml; charset=utf-8</c>. DAVx5 extracts a
/// precondition only from an XML-typed body whose root is <c>error</c> — a status served as
/// text/plain makes it fail forever instead of falling back.
/// </summary>
internal static class DavError
{
    /// <summary>
    /// Sets <paramref name="statusCode"/> and the content type before the first write — once the
    /// body has started, the header is already gone. <paramref name="condition"/> may name any
    /// namespace, not only DAV:, and <paramref name="detail"/> is written inside it verbatim
    /// (e.g. the href of a conflicting resource for no-uid-conflict).
    /// </summary>
    internal static async Task WriteAsync(HttpResponse response, int statusCode, XName condition,
        XElement? detail = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        response.StatusCode = statusCode;
        response.ContentType = "application/xml; charset=utf-8";

        var settings = new XmlWriterSettings
        {
            Async = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        var writer = XmlWriter.Create(response.Body, settings);
        await using (writer.ConfigureAwait(false))
        {
            await writer.WriteStartDocumentAsync().ConfigureAwait(false);
            await writer.WriteStartElementAsync("D", "error", DavXml.Dav.NamespaceName).ConfigureAwait(false);

            await writer.WriteStartElementAsync(null, condition.LocalName, condition.NamespaceName)
                .ConfigureAwait(false);
            if (detail is not null) await WriteElementAsync(writer, detail).ConfigureAwait(false);
            await writer.WriteEndElementAsync().ConfigureAwait(false);

            await writer.WriteEndElementAsync().ConfigureAwait(false);
            await writer.WriteEndDocumentAsync().ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
    }

    private static async Task WriteElementAsync(XmlWriter writer, XElement element)
    {
        await writer.WriteStartElementAsync(null, element.Name.LocalName, element.Name.NamespaceName)
            .ConfigureAwait(false);

        foreach (var attribute in element.Attributes())
            await writer.WriteAttributeStringAsync(null, attribute.Name.LocalName,
                attribute.Name.NamespaceName, attribute.Value).ConfigureAwait(false);

        if (element.HasElements)
            foreach (var child in element.Elements())
                await WriteElementAsync(writer, child).ConfigureAwait(false);
        else if (!string.IsNullOrEmpty(element.Value))
            await writer.WriteStringAsync(element.Value).ConfigureAwait(false);

        await writer.WriteEndElementAsync().ConfigureAwait(false);
    }
}
