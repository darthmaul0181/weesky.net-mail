using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// Writes a <c>multistatus</c> document straight into <see cref="HttpResponse.Body"/>, one
/// <c>response</c> at a time. A full address book is 5000 cards and a card may weigh 1&#160;MB, so a
/// response carrying <c>address-data</c> over the whole book is measured in gigabytes — building the
/// document in memory and serialising it would put the whole book on the heap of a process serving
/// every user.
/// </summary>
/// <remarks>
/// Implements only <see cref="IAsyncDisposable"/>, never <see cref="IDisposable"/>, so a plain
/// <c>using</c> does not compile against it — <c>await using</c> is the only spelling the compiler
/// accepts, and it is also the only correct one: the closing <c>&lt;/multistatus&gt;</c> tag is
/// written on <see cref="DisposeAsync"/>. A caller that drops a writer without ever awaiting its
/// disposal (no <c>await using</c>, no explicit call) leaves that closing tag — and the XML
/// declaration's matching end-of-document — unwritten. Nothing in the type or the runtime forces the
/// call, and the C# compiler does not warn on it either: this is a code-review-only invariant, the
/// same as any other missed <c>IAsyncDisposable</c>. What actually reaches the client in that case
/// depends only on whether <see cref="FlushAsync"/> was ever called: if it never was, the XML writer's
/// own internal buffer is discarded with it — nothing beyond the headers already set in
/// <see cref="BeginAsync"/> reaches the wire. If it was, whatever had been flushed already sits on the
/// wire as an unterminated document (open elements never closed); a conforming XML parser rejects it
/// outright, but a client that reads the response as a byte stream rather than parsing it — exactly
/// the naive-string-match behaviour this type exists to satisfy for the status line — could still read
/// a truncated, confident answer out of it.
/// </remarks>
internal sealed class MultiStatusWriter : IAsyncDisposable
{
    private static readonly XName NumberOfMatchesWithinLimits = DavXml.Dav + "number-of-matches-within-limits";

    /// <summary>
    /// How many <c>response</c> elements a streaming caller writes between pushes to the wire.
    /// Not tuned to a byte count: it exists so a long answer starts arriving early rather than in
    /// one block at the end, and a client's read timeout is counted in responses it has not seen.
    /// </summary>
    internal const int FlushEvery = 64;

    private readonly XmlWriter writer;
    private bool closed;
    private int responseCount;
    private int flushedAt;

    private MultiStatusWriter(XmlWriter writer) => this.writer = writer;

    /// <summary>
    /// How many <c>response</c> elements this document carries. "The book is empty on the client"
    /// is a claim about this number, and it is the one field an HTTP access log cannot hold.
    /// </summary>
    internal int ResponseCount => responseCount;

    /// <summary>
    /// Sets 207, the XML content type and the <c>DAV:</c> header (via <see cref="DavHeaders.ApplyDav"/>
    /// — never set by hand here), then opens <c>multistatus</c> with its three namespaces declared
    /// once on the root: redeclaring them on every <c>response</c> would double the weight of a full
    /// book's document. Nothing may be written to <paramref name="response"/> before this call: once
    /// the body has started, the status code, the content type and the header can no longer be set.
    /// Throws rather than silently losing them when the body has already started.
    /// </summary>
    internal static async Task<MultiStatusWriter> BeginAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (response.HasStarted)
            throw new InvalidOperationException(
                "MultiStatusWriter.BeginAsync was called after the response body had already " +
                "started; the status code, the content type and the DAV header can no longer be set.");

        response.StatusCode = StatusCodes.Status207MultiStatus;
        response.ContentType = DavHeaders.XmlContentType;
        DavHeaders.ApplyDav(response);

        var settings = new XmlWriterSettings
        {
            Async = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CloseOutput = false, // Response.Body belongs to ASP.NET Core, never to this writer.
        };

        var xmlWriter = XmlWriter.Create(response.Body, settings);
        await xmlWriter.WriteStartDocumentAsync().ConfigureAwait(false);
        await xmlWriter.WriteStartElementAsync("D", "multistatus", DavXml.Dav.NamespaceName).ConfigureAwait(false);
        await xmlWriter.WriteAttributeStringAsync("xmlns", "C", null, DavXml.CardDav.NamespaceName)
            .ConfigureAwait(false);
        await xmlWriter.WriteAttributeStringAsync("xmlns", "CS", null, DavXml.CalendarServer.NamespaceName)
            .ConfigureAwait(false);

        return new MultiStatusWriter(xmlWriter);
    }

    /// <summary>
    /// One <c>response</c> carrying properties. The <paramref name="found"/> propstat is written
    /// BEFORE the <paramref name="missing"/> one — the first of the three literal invariants:
    /// Thunderbird reads the FIRST descendant <c>status</c> of a <c>response</c> and compares it to
    /// the string <c>"HTTP/1.1 200 OK"</c>; writing the 404 propstat first makes every response read
    /// as a failure. Either list may be empty, in which case its propstat is omitted entirely.
    /// </summary>
    internal async Task WriteResourceAsync(string href, IReadOnlyList<XElement> found,
        IReadOnlyList<XName> missing, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await writer.WriteStartElementAsync(null, "response", DavXml.Dav.NamespaceName).ConfigureAwait(false);
        await WriteHrefAsync(href).ConfigureAwait(false);

        if (found.Count > 0)
            await WritePropstatAsync(200, found, cancellationToken).ConfigureAwait(false);
        if (missing.Count > 0)
            await WritePropstatAsync(404, missing.Select(name => new XElement(name)), cancellationToken)
                .ConfigureAwait(false);

        await writer.WriteEndElementAsync().ConfigureAwait(false); // response
        responseCount++;
    }

    /// <summary>
    /// One <c>response</c> whose <c>status</c> is a DIRECT child — never lodged inside a
    /// <c>propstat</c>. The shape a sync-collection tombstone takes (plan c); written here rather
    /// than there so the literality lives in one place.
    /// </summary>
    internal async Task WriteStatusAsync(string href, int statusCode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await writer.WriteStartElementAsync(null, "response", DavXml.Dav.NamespaceName).ConfigureAwait(false);
        await WriteHrefAsync(href).ConfigureAwait(false);
        await writer.WriteElementStringAsync(null, "status", DavXml.Dav.NamespaceName, StatusLine(statusCode))
            .ConfigureAwait(false);
        await writer.WriteEndElementAsync().ConfigureAwait(false); // response
        responseCount++;
    }

    /// <summary>
    /// The truncation shape of RFC 6352 § 8.6.2: a <c>response</c> on the Request-URI carrying 507
    /// and <c>number-of-matches-within-limits</c> inside <c>error</c> — not a bare 403, which rests
    /// on no text a client can act on.
    /// </summary>
    internal async Task WriteTruncatedAsync(string href, XElement? extra, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await writer.WriteStartElementAsync(null, "response", DavXml.Dav.NamespaceName).ConfigureAwait(false);
        await WriteHrefAsync(href).ConfigureAwait(false);
        await writer.WriteElementStringAsync(null, "status", DavXml.Dav.NamespaceName, StatusLine(507))
            .ConfigureAwait(false);

        await writer.WriteStartElementAsync(null, "error", DavXml.Dav.NamespaceName).ConfigureAwait(false);
        await writer.WriteStartElementAsync(null, NumberOfMatchesWithinLimits.LocalName,
            NumberOfMatchesWithinLimits.NamespaceName).ConfigureAwait(false);
        await writer.WriteEndElementAsync().ConfigureAwait(false); // number-of-matches-within-limits
        if (extra is not null) await writer.WriteElementAsync(extra).ConfigureAwait(false);
        await writer.WriteEndElementAsync().ConfigureAwait(false); // error

        await writer.WriteEndElementAsync().ConfigureAwait(false); // response
        responseCount++;
    }

    /// <summary>
    /// One <c>response</c> whose single <c>propstat</c> refuses every named property with 403 —
    /// RFC 4918 § 9.2.1's answer for a property a server does not let a client write. The names
    /// carry no value: a <c>propstat</c>'s <c>prop</c> names properties, it never restates them.
    /// An empty list writes the href alone, because nothing was refused when nothing was asked.
    /// </summary>
    internal async Task WriteRefusalAsync(string href, IReadOnlyList<XName> names,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await writer.WriteStartElementAsync(null, "response", DavXml.Dav.NamespaceName).ConfigureAwait(false);
        await WriteHrefAsync(href).ConfigureAwait(false);
        if (names.Count > 0)
        {
            await WritePropstatAsync(403, names.Select(name => new XElement(name)), cancellationToken)
                .ConfigureAwait(false);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false); // response
        responseCount++;
    }

    /// <summary>
    /// The <c>DAV:sync-token</c> of RFC 6578 § 6.2, a direct child of <c>multistatus</c> written
    /// after the last response — the value the client files and hands back next round.
    /// </summary>
    internal async Task WriteSyncTokenAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await writer.WriteElementStringAsync(null, "sync-token", DavXml.Dav.NamespaceName, token)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Pushes what is written so far onto the wire, and only that: the point of writing straight
    /// into the body is that the first response is sent before the last one is composed.
    /// </summary>
    internal async Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        flushedAt = responseCount;
        await writer.FlushAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Pushes once <see cref="FlushEvery"/> further responses have been written — what a streaming
    /// loop calls each turn so the policy lives here rather than in an arithmetic every caller
    /// could spell differently.
    /// </summary>
    /// <remarks>
    /// A flush is what makes <see cref="HttpResponse.HasStarted"/> true, and from that moment a
    /// refusal can no longer replace this document: <see cref="DavError.WriteAsync"/> drops it with
    /// a warning rather than write into a started body. Every refusal of these reports is pronounced
    /// before <see cref="BeginAsync"/>, so there is nothing left to replace by the time a loop turns.
    /// <b>A loop reading inside a transaction must NOT call this.</b> Flushing hands the pace of the
    /// read to whoever is draining the socket, and a caller that holds an InnoDB snapshot open would
    /// be making a slow client the reason it stays open — the opposite of what this buys elsewhere.
    /// </remarks>
    internal Task FlushIfDueAsync(CancellationToken cancellationToken) =>
        responseCount - flushedAt >= FlushEvery ? FlushAsync(cancellationToken) : Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (closed) return;
        closed = true;

        await writer.WriteEndElementAsync().ConfigureAwait(false); // multistatus
        await writer.WriteEndDocumentAsync().ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
        await writer.DisposeAsync().ConfigureAwait(false);
    }

    private Task WriteHrefAsync(string href) =>
        writer.WriteElementStringAsync(null, "href", DavXml.Dav.NamespaceName, href);

    private async Task WritePropstatAsync(int statusCode, IEnumerable<XElement> properties,
        CancellationToken cancellationToken)
    {
        await writer.WriteStartElementAsync(null, "propstat", DavXml.Dav.NamespaceName).ConfigureAwait(false);
        await writer.WriteStartElementAsync(null, "prop", DavXml.Dav.NamespaceName).ConfigureAwait(false);

        foreach (var property in properties)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteElementAsync(property).ConfigureAwait(false);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false); // prop
        await writer.WriteElementStringAsync(null, "status", DavXml.Dav.NamespaceName, StatusLine(statusCode))
            .ConfigureAwait(false);
        await writer.WriteEndElementAsync().ConfigureAwait(false); // propstat
    }

    /// <summary>
    /// Code to status-line table, literal — deliberately not <c>ReasonPhrases.GetReasonPhrase</c>:
    /// that framework table has already changed case between versions, and these strings are
    /// compared byte for byte by at least one client (sabre has had to correct an "Ok" for iOS). Internal
    /// so the nested responses of expand-property spell the very same lines.
    /// </summary>
    internal static string StatusLine(int statusCode) => statusCode switch
    {
        200 => "HTTP/1.1 200 OK",
        201 => "HTTP/1.1 201 Created",
        204 => "HTTP/1.1 204 No Content",
        400 => "HTTP/1.1 400 Bad Request",
        401 => "HTTP/1.1 401 Unauthorized",
        403 => "HTTP/1.1 403 Forbidden",
        404 => "HTTP/1.1 404 Not Found",
        409 => "HTTP/1.1 409 Conflict",
        412 => "HTTP/1.1 412 Precondition Failed",
        423 => "HTTP/1.1 423 Locked",
        424 => "HTTP/1.1 424 Failed Dependency",
        500 => "HTTP/1.1 500 Internal Server Error",
        502 => "HTTP/1.1 502 Bad Gateway",
        507 => "HTTP/1.1 507 Insufficient Storage",
        _ => throw new ArgumentOutOfRangeException(nameof(statusCode), statusCode,
            "No literal HTTP/1.1 status line is registered for this code."),
    };
}
