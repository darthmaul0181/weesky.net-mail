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
    /// <remarks>
    /// A response that has already started is a silent no-op here, not a thrown
    /// <see cref="InvalidOperationException"/> — deliberately the opposite of
    /// <c>MultiStatusWriter.BeginAsync</c>'s guard. <c>BeginAsync</c> only ever runs at the top of
    /// a fresh response, so a started body there is always a caller bug, worth surfacing loudly.
    /// This method exists specifically to be reachable from a <em>failed</em> write already in
    /// progress — tasks 6 to 11 call it from inside a <c>multistatus</c> stream that may have
    /// begun. Throwing there would turn one already-imperfect response (a document truncated
    /// mid-write) into an unhandled exception on top of it — exactly the 500 this whole type
    /// exists to avoid. A client holding a truncated document is not helped by a second failure;
    /// it is helped by nothing more being attempted on a response that can no longer carry it.
    /// </remarks>
    /// <param name="response">the response to write the error document into</param>
    /// <param name="statusCode">the HTTP status to set before writing</param>
    /// <param name="condition">the precondition or postcondition element name</param>
    /// <param name="detail">written inside the condition element verbatim, when there is one</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <param name="logger">
    /// Traces the quiet return. The no-op is deliberate mid-multistatus, but a caller reaching here
    /// on a response started for any other reason has a bug, and without this line it leaves no
    /// trace at all — the same silent-conversion gap <see cref="DavXmlReader.ParseAsync"/> closes.
    /// </param>
    internal static async Task WriteAsync(HttpResponse response, int statusCode, XName condition,
        XElement? detail = null, CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (response.HasStarted)
        {
            logger?.LogWarning(
                "A CardDAV error document was dropped: the response had already started with {Status}",
                response.StatusCode);
            return;
        }

        response.StatusCode = statusCode;
        response.ContentType = DavHeaders.XmlContentType;

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
            if (detail is not null) await writer.WriteElementAsync(detail).ConfigureAwait(false);
            await writer.WriteEndElementAsync().ConfigureAwait(false);

            await writer.WriteEndElementAsync().ConfigureAwait(false);
            await writer.WriteEndDocumentAsync().ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
    }
}
