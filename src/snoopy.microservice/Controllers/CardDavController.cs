using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.CardDav;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// The CardDAV surface. Deliberately NOT <c>[ApiController]</c>: its binding conventions and its
/// automatic 400 on an invalid ModelState would pre-empt this protocol's own responses. The policy
/// is named on purpose — a bare <c>[Authorize]</c> would challenge with <c>WWW-Authenticate:
/// Bearer</c>, which a CardDAV client has no token for and no way to ask for one. Hidden from the
/// API explorer: Swashbuckle has no OpenAPI operation type for a PROPFIND and throws at scan time.
/// </summary>
[Route("dav")]
[Authorize(Policy = CardDavAuthenticationDefaults.PolicyName)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class CardDavController(
    IDavContactReader contacts,
    IContactSyncStore syncStore,
    ILogger<CardDavController> logger) : ApiBaseController
{
    private const int MaxBodyBytes = 1024 * 1024;

    private static readonly XName FiniteDepth = DavXml.Dav + "propfind-finite-depth";
    private static readonly XName SupportedReport = DavXml.Dav + "supported-report";

    [AcceptVerbs("PROPFIND", Route = "")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task PropfindServiceRootAsync(CancellationToken cancellationToken) =>
        PropfindAsync(DavResourceKind.ServiceRoot, null, null, DavPaths.Root + "/", cancellationToken);

    /// <summary>
    /// The bare root, OUTSIDE /dav but under the same policy: a client given the bare host tries
    /// "/" as much as the well-known, and a Bearer challenge there is the symptom the named policy
    /// exists to prevent.
    /// </summary>
    [AcceptVerbs("PROPFIND", Route = "/")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task PropfindBareRootAsync(CancellationToken cancellationToken) =>
        PropfindAsync(DavResourceKind.ServiceRoot, null, null, "/", cancellationToken);

    [AcceptVerbs("PROPFIND", Route = "principals/{userId:guid}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task PropfindPrincipalAsync(Guid userId, CancellationToken cancellationToken) =>
        PropfindAsync(DavResourceKind.Principal, userId, null, null, cancellationToken);

    [AcceptVerbs("PROPFIND", Route = "addressbooks/{userId:guid}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task PropfindHomeAsync(Guid userId, CancellationToken cancellationToken) =>
        PropfindAsync(DavResourceKind.Home, userId, null, null, cancellationToken);

    [AcceptVerbs("PROPFIND", Route = "addressbooks/{userId:guid}/" + DavPaths.BookName)]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task PropfindCollectionAsync(Guid userId, CancellationToken cancellationToken) =>
        PropfindAsync(DavResourceKind.Collection, userId, null, null, cancellationToken);

    [AcceptVerbs("PROPFIND", Route = "addressbooks/{userId:guid}/" + DavPaths.BookName + "/{*davName}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task PropfindCardAsync(Guid userId, string? davName, CancellationToken cancellationToken) =>
        PropfindAsync(DavResourceKind.Card, userId, davName, null, cancellationToken);

    /// <summary>
    /// PROPPATCH — the one non-mutating method here that is NOT a 405, on every resource shape the
    /// <c>Allow</c> header announces it on. Two reasons, and either alone would be enough.
    /// <c>DAV: 1</c> engages: RFC 4918 § 18.1 makes class 1 the satisfaction of every MUST of the
    /// document, and § 9.2 requires PROPPATCH of every conforming resource — a 405 where our own
    /// <c>Allow</c> names the verb is a header that lies, and a client reading it loops. And Apple's
    /// Contacts.app PROPPATCHes <c>{calendarserver}me-card</c> on the address HOME, not on the book;
    /// sabre documents that not supporting it can make that client CRASH, not merely abandon the
    /// book. The answer is § 9.2.1's for a property one does not let write — a 207 whose every
    /// propstat carries <c>403 Forbidden</c> — and nothing is stored on the way through: serving a
    /// dead property would want a row per client whim, for a use no screen of the product renders.
    /// </summary>
    [AcceptVerbs("PROPPATCH", Route = "")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task ProppatchServiceRootAsync(CancellationToken cancellationToken) =>
        ProppatchAsync(DavResourceKind.ServiceRoot, null, null, DavPaths.Root + "/", cancellationToken);

    [AcceptVerbs("PROPPATCH", Route = "/")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task ProppatchBareRootAsync(CancellationToken cancellationToken) =>
        ProppatchAsync(DavResourceKind.ServiceRoot, null, null, "/", cancellationToken);

    [AcceptVerbs("PROPPATCH", Route = "principals/{userId:guid}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task ProppatchPrincipalAsync(Guid userId, CancellationToken cancellationToken) =>
        ProppatchAsync(DavResourceKind.Principal, userId, null, null, cancellationToken);

    [AcceptVerbs("PROPPATCH", Route = "addressbooks/{userId:guid}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task ProppatchHomeAsync(Guid userId, CancellationToken cancellationToken) =>
        ProppatchAsync(DavResourceKind.Home, userId, null, null, cancellationToken);

    [AcceptVerbs("PROPPATCH", Route = "addressbooks/{userId:guid}/" + DavPaths.BookName)]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task ProppatchCollectionAsync(Guid userId, CancellationToken cancellationToken) =>
        ProppatchAsync(DavResourceKind.Collection, userId, null, null, cancellationToken);

    [AcceptVerbs("PROPPATCH", Route = "addressbooks/{userId:guid}/" + DavPaths.BookName + "/{*davName}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task ProppatchCardAsync(Guid userId, string? davName, CancellationToken cancellationToken) =>
        ProppatchAsync(DavResourceKind.Card, userId, davName, null, cancellationToken);

    private async Task ProppatchAsync(DavResourceKind kind, Guid? userId, string? davName,
        string? rootHref, CancellationToken cancellationToken)
    {
        var responses = 0;
        try
        {
            var user = AuthenticatedUser;
            if (userId is { } target && target != user.WebmailUid)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (userId is { } owner && RedirectedToCanonical(kind, owner)) return;

            IReadOnlyList<XName> names;
            try
            {
                names = DavPropertyUpdate.NamesIn(
                    await DavXmlReader.ParseAsync(Request.Body, cancellationToken, logger));
            }
            catch (DavBadRequestException ex)
            {
                logger.LogInformation("PROPPATCH body refused: {Reason}", ex.Message);
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            DavCard? card = null;
            if (kind is DavResourceKind.Card)
            {
                // Answering 207 on a name that designates nothing would tell the client the card
                // exists — the same lie PROPFIND and GET refuse to tell here.
                card = DavName.IsValid(davName)
                    ? await contacts.FindAsync(user.WebmailUid, davName!, cancellationToken)
                    : null;
                if (card is null)
                {
                    Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
            }

            await using var writer = await MultiStatusWriter.BeginAsync(Response, cancellationToken);
            await writer.WriteRefusalAsync(
                HrefOf(kind, user.WebmailUid, card?.DavName, rootHref), names, cancellationToken);
            responses = writer.ResponseCount;
        }
        finally
        {
            LogRequest(responses: responses);
        }
    }

    /// <summary>
    /// The card, verbatim. HEAD is bound to the same action, so it answers the same headers by
    /// construction — Content-Length included, which is what makes it worth issuing.
    /// </summary>
    [HttpGet("addressbooks/{userId:guid}/" + DavPaths.BookName + "/{*davName}")]
    [HttpHead("addressbooks/{userId:guid}/" + DavPaths.BookName + "/{*davName}")]
    public async Task GetCardAsync(Guid userId, string? davName, CancellationToken cancellationToken)
    {
        var served = 0;
        try
        {
            var user = AuthenticatedUser;
            if (userId != user.WebmailUid)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // Ruling AM: routing has already decoded any %2F into the '/' IsValid refuses, and an
            // invalid name designates nothing — the same 404 an unknown name gets, never a 400.
            var card = DavName.IsValid(davName)
                ? await contacts.FindAsync(user.WebmailUid, davName!, cancellationToken)
                : null;
            if (card is null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var entityTag = DavProperties.EntityTag(card);
            Response.Headers.ETag = entityTag;
            Response.Headers.LastModified = DavProperties.HttpDate(card.UpdatedAt);
            DavHeaders.ApplyDav(Response);

            if (EntityTagMatcher.NoneMatch(Request.Headers.IfNoneMatch, entityTag))
            {
                Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            // Never through a formatter: a re-encode — a BOM, a line ending, a charset — would leave
            // the ETag describing something other than what goes out. GetBytes emits no preamble.
            var bytes = Encoding.UTF8.GetBytes(card.VCardRaw);
            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = DavHeaders.VCardContentType;
            Response.ContentLength = bytes.Length;
            served = 1;

            // Explicit rather than left to the host: Kestrel drops a HEAD body, TestServer does not.
            if (HttpMethods.IsHead(Request.Method)) return;

            await Response.Body.WriteAsync(bytes, cancellationToken);
        }
        finally
        {
            LogRequest(responses: served);
        }
    }

    /// <summary>
    /// Generic WebDAV clients GET the collection. Without this the card route's <c>{*davName}</c>
    /// would answer a routing 404 on a URL that does not present that segment; a 405 naming the
    /// verbs is an answer every client knows how to file.
    /// </summary>
    [HttpGet("addressbooks/{userId:guid}/" + DavPaths.BookName)]
    [HttpHead("addressbooks/{userId:guid}/" + DavPaths.BookName)]
    public void GetCollection(Guid userId)
    {
        if (userId != AuthenticatedUser.WebmailUid)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
        }
        else
        {
            Response.Headers.Allow = DavHeaders.CollectionAllow;
            DavHeaders.ApplyDav(Response);
            Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        }

        LogRequest();
    }

    [AcceptVerbs("REPORT", Route = "principals/{userId:guid}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task ReportPrincipalAsync(Guid userId, CancellationToken cancellationToken) =>
        ReportAsync(DavResourceKind.Principal, userId, null, cancellationToken);

    [AcceptVerbs("REPORT", Route = "addressbooks/{userId:guid}/" + DavPaths.BookName)]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task ReportCollectionAsync(Guid userId, CancellationToken cancellationToken) =>
        ReportAsync(DavResourceKind.Collection, userId, null, cancellationToken);

    /// <summary>RFC 6352 § 8.7 defines multiget on address resources too, and
    /// supported-report-set says so on every card — without this route the header lies.</summary>
    [AcceptVerbs("REPORT", Route = "addressbooks/{userId:guid}/" + DavPaths.BookName + "/{*davName}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task ReportCardAsync(Guid userId, string? davName, CancellationToken cancellationToken) =>
        ReportAsync(DavResourceKind.Card, userId, davName, cancellationToken);

    private async Task ReportAsync(DavResourceKind kind, Guid userId, string? davName,
        CancellationToken cancellationToken)
    {
        string? report = null;
        string? condition = null;
        var responses = 0;
        try
        {
            var user = AuthenticatedUser;
            if (userId != user.WebmailUid)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (RedirectedToCanonical(kind, userId)) return;

            if (kind is DavResourceKind.Card && !DavName.IsValid(davName))
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            XDocument? document;
            try
            {
                document = await DavXmlReader.ParseAsync(Request.Body, cancellationToken, logger);
            }
            catch (DavBadRequestException ex)
            {
                logger.LogInformation("REPORT body refused: {Reason}", ex.Message);
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // The name the client wrote, not the kind we recognised: a report we do not serve is
            // exactly the case the line has to name, and "Unknown" would erase which one it was.
            report = document?.Root?.Name.LocalName;

            // The Depth header is deliberately ignored, never refused: PROPFIND's rule is PROPFIND's
            // alone — a report already says what it applies to, so there is nothing to guess.
            var requestHref = kind switch
            {
                DavResourceKind.Principal => DavPaths.Principal(user.WebmailUid),
                DavResourceKind.Collection => DavPaths.Collection(user.WebmailUid),
                _ => DavPaths.Card(user.WebmailUid, davName!),
            };

            try
            {
                switch (document is null ? DavReportKind.Unknown : ReportRequest.KindOf(document))
                {
                    case DavReportKind.Multiget:
                        responses = await MultigetReport.WriteAsync(Response, document!, requestHref,
                            user.WebmailUid, user.Email, contacts, cancellationToken);
                        return;
                    case DavReportKind.ExpandProperty:
                        responses = await ExpandAsync(kind, davName, document!, requestHref,
                            cancellationToken);
                        return;
                    default:
                        // Query, SyncCollection and Unknown alike, through this one branch: plan c
                        // implements the first two by removing their cases, and a report we do not
                        // serve is a considered 403 — a 500 makes a client loop on it forever.
                        condition = SupportedReport.LocalName;
                        await DavError.WriteAsync(Response, StatusCodes.Status403Forbidden,
                            SupportedReport, cancellationToken: cancellationToken, logger: logger);
                        return;
                }
            }
            catch (DavPreconditionException ex)
            {
                condition = ex.Condition.LocalName;
                await DavError.WriteAsync(Response, StatusCodes.Status403Forbidden, ex.Condition,
                    ex.Detail, cancellationToken, logger);
            }
        }
        finally
        {
            LogRequest(report, responses, condition);
        }
    }

    private async Task<int> ExpandAsync(DavResourceKind kind, string? davName, XDocument document,
        string requestHref, CancellationToken cancellationToken)
    {
        var user = AuthenticatedUser;
        DavCard? card = null;
        if (kind is DavResourceKind.Card)
        {
            card = await contacts.FindAsync(user.WebmailUid, davName!, cancellationToken);
            if (card is null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return 0;
            }
        }

        var state = kind is DavResourceKind.Collection
            ? await syncStore.ReadStateAsync(user.WebmailUid, cancellationToken)
            : null;
        var target = new DavResourceContext(kind, user.WebmailUid, user.Email, card, state);
        return await ExpandPropertyReport.WriteAsync(Response, document, target, requestHref,
            resource => NestedContext(resource, user.WebmailUid, user.Email), cancellationToken);
    }

    /// <summary>
    /// The context a nested expand-property target resolves against, or null for anything that is
    /// not this user's — the nested 404. No href property of ours points at a card or needs the
    /// sync state, so neither is fetched here.
    /// </summary>
    private static DavResourceContext? NestedContext(DavResource resource, Guid userId, string email)
    {
        if (resource.Kind is DavResourceKind.ServiceRoot)
            return new DavResourceContext(DavResourceKind.ServiceRoot, userId, email, null, null);
        if (resource.Kind is DavResourceKind.Card || resource.UserId != userId) return null;
        return new DavResourceContext(resource.Kind, userId, email, null, null);
    }

    private async Task PropfindAsync(DavResourceKind kind, Guid? userId, string? davName,
        string? rootHref, CancellationToken cancellationToken)
    {
        var responses = 0;
        string? condition = null;
        try
        {
            var user = AuthenticatedUser;

            // Ownership first: a foreign {userId} answers 404, never 403 — a 403 would confirm the
            // existence of the principal aimed at.
            if (userId is { } target && target != user.WebmailUid)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (userId is { } owner && RedirectedToCanonical(kind, owner)) return;

            var depth = DavDepth.Parse(DepthHeader());
            if (depth is null)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (depth is DavDepthValue.Infinity)
            {
                condition = FiniteDepth.LocalName;
                await DavError.WriteAsync(Response, StatusCodes.Status403Forbidden, FiniteDepth,
                    cancellationToken: cancellationToken, logger: logger);
                return;
            }

            var request = await ReadRequestAsync(cancellationToken);
            if (request is null) return; // the 400 is already on the response

            DavCard? card = null;
            if (kind is DavResourceKind.Card)
            {
                // An invalid name — routing has already decoded any %2F into the '/' IsValid
                // refuses — designates nothing, the same 404 an unknown name gets.
                card = DavName.IsValid(davName)
                    ? await contacts.FindAsync(user.WebmailUid, davName!, cancellationToken)
                    : null;
                if (card is null)
                {
                    Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
            }

            var state = NeedsState(kind, depth.Value)
                ? await syncStore.ReadStateAsync(user.WebmailUid, cancellationToken)
                : null;

            var resource = new DavResourceContext(kind, user.WebmailUid, user.Email, card, state);
            var href = HrefOf(kind, user.WebmailUid, card?.DavName, rootHref);

            await using var writer = await MultiStatusWriter.BeginAsync(Response, cancellationToken);
            try
            {
                await WriteResourceAsync(writer, href, request, resource, cancellationToken);

                if (depth is DavDepthValue.One && kind is DavResourceKind.Home)
                {
                    await WriteResourceAsync(writer, DavPaths.Collection(user.WebmailUid), request,
                        resource with { Kind = DavResourceKind.Collection }, cancellationToken);
                }

                if (depth is DavDepthValue.One && kind is DavResourceKind.Collection)
                {
                    await foreach (var member in contacts.StreamAsync(user.WebmailUid, cancellationToken))
                    {
                        await WriteResourceAsync(writer, DavPaths.Card(user.WebmailUid, member.DavName),
                            request, resource with { Kind = DavResourceKind.Card, Card = member },
                            cancellationToken);
                    }
                }
            }
            finally
            {
                // Read off the writer, not counted here: a book that streamed halfway before the
                // connection died still says how far it got, which is the whole point of the line.
                responses = writer.ResponseCount;
            }
        }
        finally
        {
            LogRequest(responses: responses, condition: condition);
        }
    }

    /// <summary>
    /// Null means the 400 is already on the response. A BadHttpRequestException — the
    /// [RequestSizeLimit] tripping — flies through untouched: Kestrel's 413, not our 400.
    /// </summary>
    private async Task<DavPropertyRequest?> ReadRequestAsync(CancellationToken cancellationToken)
    {
        try
        {
            return DavPropertyRequest.Parse(
                await DavXmlReader.ParseAsync(Request.Body, cancellationToken, logger));
        }
        catch (DavBadRequestException ex)
        {
            logger.LogInformation("PROPFIND body refused: {Reason}", ex.Message);
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return null;
        }
    }

    /// <summary>
    /// A collection URL keeps its trailing slash — without it <see cref="DavPaths.Parse"/>
    /// designates nothing — and the answer is a 308, never the 301 sabre and Radicale use: a 301
    /// lets the client replay as GET, which bare OkHttp does for every verb but PROPFIND, and a
    /// redirected REPORT would lose both its method and its body.
    /// </summary>
    private bool RedirectedToCanonical(DavResourceKind kind, Guid userId)
    {
        var canonical = kind switch
        {
            DavResourceKind.Principal => DavPaths.Principal(userId),
            DavResourceKind.Home => DavPaths.Home(userId),
            DavResourceKind.Collection => DavPaths.Collection(userId),
            _ => null,
        };
        if (canonical is null || Request.Path.Value?.EndsWith('/') is not false) return false;

        Response.Headers.Location = canonical;
        Response.StatusCode = StatusCodes.Status308PermanentRedirect;
        return true;
    }

    private string? DepthHeader() =>
        Request.Headers.TryGetValue("Depth", out var values) ? values.ToString() : null;

    /// <summary>The href a response names this resource by — one table, so PROPFIND and PROPPATCH
    /// cannot spell the same resource two ways.</summary>
    private static string HrefOf(DavResourceKind kind, Guid userId, string? davName, string? rootHref) =>
        kind switch
        {
            DavResourceKind.ServiceRoot => rootHref!,
            DavResourceKind.Principal => DavPaths.Principal(userId),
            DavResourceKind.Home => DavPaths.Home(userId),
            DavResourceKind.Collection => DavPaths.Collection(userId),
            _ => DavPaths.Card(userId, davName!),
        };

    /// <summary>
    /// The one line this request leaves, written from every action and on every path out of it —
    /// the error paths above all, since a failure of this protocol reaches the user as a book that
    /// is simply empty, with nothing on the server saying which of its five causes it was. Called
    /// from a <c>finally</c> so a throw still leaves the trace, and read off
    /// <see cref="HttpResponse.StatusCode"/> rather than told, so it can never claim a status the
    /// response does not carry. The path, never the query: the query is where a token travels.
    /// </summary>
    private void LogRequest(string? report = null, int responses = 0, string? condition = null) =>
        DavRequestLog.Write(logger, new DavRequestTrace(
            Request.Method, Request.Path.Value ?? string.Empty, DepthHeader(), report,
            TokenIn: null, TokenOut: null, responses, Response.StatusCode, condition));

    private static async Task WriteResourceAsync(MultiStatusWriter writer, string href,
        DavPropertyRequest request, DavResourceContext resource, CancellationToken cancellationToken)
    {
        var (found, missing) = DavProperties.Resolve(request, resource);
        await writer.WriteResourceAsync(href, found, missing, cancellationToken);
    }

    /// <summary>Only the collection's properties read the sync state; fetch it only when one of
    /// this answer's resources is the collection.</summary>
    private static bool NeedsState(DavResourceKind kind, DavDepthValue depth) =>
        kind is DavResourceKind.Collection
        || (kind is DavResourceKind.Home && depth is DavDepthValue.One);

    /// <summary>
    /// Capabilities, answered off the URL shape alone: <c>[AllowAnonymous]</c> on this method and
    /// on no other, because a client asks what the server can do before it holds any credentials —
    /// which is also why it consults no store and reveals nothing a URL did not already carry.
    /// </summary>
    [AcceptVerbs("OPTIONS", Route = "")]
    [AcceptVerbs("OPTIONS", Route = "/")]
    [AcceptVerbs("OPTIONS", Route = "principals/{userId:guid}")]
    [AcceptVerbs("OPTIONS", Route = "addressbooks/{userId:guid}")]
    [AcceptVerbs("OPTIONS", Route = "addressbooks/{userId:guid}/" + DavPaths.BookName)]
    [AllowAnonymous]
    public void OptionsCollection() => Capabilities(DavHeaders.CollectionAllow);

    [AcceptVerbs("OPTIONS", Route = "addressbooks/{userId:guid}/" + DavPaths.BookName + "/{*davName}")]
    [AllowAnonymous]
    public void OptionsCard() => Capabilities(DavHeaders.CardAllow);

    /// <summary>
    /// Last on purpose, and bound to no verb: carrying no method metadata, these score below every
    /// real route above, so action selection reaches them only when nothing else answers the verb.
    /// They carry <c>Allow</c> and nothing else. Routing supplies an Allow of its own, but it is
    /// the union of the verbs bound on the template: it names GET and HEAD, which answer 405 on a
    /// collection, and omits PUT and DELETE, which a card announces — either way a client that
    /// reads it is told something the surface does not do.
    /// </summary>
    [Route("")]
    [Route("principals/{userId:guid}")]
    [Route("addressbooks/{userId:guid}")]
    [Route("addressbooks/{userId:guid}/" + DavPaths.BookName)]
    public void MethodNotAllowedOnCollection() => MethodNotAllowed(DavHeaders.CollectionAllow);

    [Route("addressbooks/{userId:guid}/" + DavPaths.BookName + "/{*davName}")]
    public void MethodNotAllowedOnCard() => MethodNotAllowed(DavHeaders.CardAllow);

    private void Capabilities(string allow)
    {
        Response.Headers.Allow = allow;
        DavHeaders.ApplyDav(Response);
        Response.StatusCode = StatusCodes.Status200OK;
        LogRequest();
    }

    private void MethodNotAllowed(string allow)
    {
        Response.Headers.Allow = allow;
        Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        LogRequest();
    }
}
