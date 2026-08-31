using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Data.Preferences;
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
[NoFormBinding]
public sealed class CardDavController(
    IDavContactReader contacts,
    IDavContactWriter writer,
    IContactSyncStore syncStore,
    PreferencesDbContext preferences,
    ILogger<CardDavController> logger) : ApiBaseController
{
    private const int MaxBodyBytes = 1024 * 1024;

    /// <summary>Above the card ceiling, so a body over it is read and refused as the announced
    /// <c>403 max-resource-size</c> rather than a transport 413 the announcement never named.</summary>
    private const int PutBodyBytes = 2 * ContactStore.MaxCardBytes;

    private const string CardRoute = "addressbooks/{userId:guid}/" + DavPaths.BookName + "/{*davName}";
    private const string CollectionRoute = "addressbooks/{userId:guid}/" + DavPaths.BookName;

    private static readonly XName FiniteDepth = DavXml.Dav + "propfind-finite-depth";
    private static readonly XName SupportedReport = DavXml.Dav + "supported-report";
    private static readonly XName ValidAddressData = DavXml.CardDav + "valid-address-data";

    /// <summary>
    /// The three reading verbs, one action per shape. PROPPATCH is the one non-mutating method
    /// here that is NOT a 405, on every shape the <c>Allow</c> header announces it on: RFC 4918
    /// § 9.2 requires it of every conforming resource, and Apple's Contacts.app PROPPATCHes
    /// <c>{calendarserver}me-card</c> on the address HOME — sabre documents that refusing it can
    /// make that client crash. The answer is § 9.2.1's for a property one does not let write, a 207
    /// whose every propstat carries 403, and nothing is stored on the way through. REPORT is bound
    /// on the home and the service root too, where a 405 under our own <c>Allow</c> would make an
    /// RFC 9110 client retry the verb for ever; the default branch's <c>403 supported-report</c>
    /// is the considered answer there, and expand-property genuinely serves the root's principal.
    /// </summary>
    [AcceptVerbs("PROPFIND", "PROPPATCH", "REPORT", Route = "")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task ServiceRootAsync(CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.ServiceRoot, null, null, DavPaths.Root + "/", cancellationToken);

    /// <summary>
    /// The bare root, OUTSIDE /dav but under the same policy: a client given the bare host tries
    /// "/" as much as the well-known, and a Bearer challenge there is the symptom the named policy
    /// exists to prevent. REPORT stays unbound on purpose: no catch-all of ours answers there, so
    /// its 405 carries routing's own Allow, which honestly omits the verb.
    /// </summary>
    [AcceptVerbs("PROPFIND", "PROPPATCH", Route = "/")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task BareRootAsync(CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.ServiceRoot, null, null, "/", cancellationToken);

    [AcceptVerbs("PROPFIND", "PROPPATCH", "REPORT", Route = "principals/{userId:guid}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task PrincipalAsync(Guid userId, CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.Principal, userId, null, null, cancellationToken);

    [AcceptVerbs("PROPFIND", "PROPPATCH", "REPORT", Route = "addressbooks/{userId:guid}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task HomeAsync(Guid userId, CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.Home, userId, null, null, cancellationToken);

    [AcceptVerbs("PROPFIND", "PROPPATCH", "REPORT", Route = CollectionRoute)]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task CollectionAsync(Guid userId, CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.Collection, userId, null, null, cancellationToken);

    /// <summary>RFC 6352 § 8.6 and § 8.7 define query and multiget on address resources too, and
    /// supported-report-set says so on every card — without REPORT here the header lies.</summary>
    [AcceptVerbs("PROPFIND", "PROPPATCH", "REPORT", Route = CardRoute)]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task CardAsync(Guid userId, string? davName, CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.Card, userId, davName, null, cancellationToken);

    private Task DispatchAsync(DavResourceKind kind, Guid? userId, string? davName, string? rootHref,
        CancellationToken cancellationToken) => Request.Method switch
    {
        "PROPFIND" => PropfindAsync(kind, userId, davName, rootHref, cancellationToken),
        "PROPPATCH" => ProppatchAsync(kind, userId, davName, rootHref, cancellationToken),
        _ => ReportAsync(kind, userId, davName, rootHref, cancellationToken),
    };

    /// <summary>
    /// The card, verbatim. HEAD is bound to the same action, so it answers the same headers by
    /// construction — Content-Length included, which is what makes it worth issuing.
    /// </summary>
    [HttpGet(CardRoute)]
    [HttpHead(CardRoute)]
    public Task GetCardAsync(Guid userId, string? davName, CancellationToken cancellationToken) =>
        TracedAsync(userId, DavResourceKind.Card, async trace =>
        {
            if (await FindCardOr404Async(davName, cancellationToken) is not { } card) return;

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
            trace.Responses = 1;

            // Explicit rather than left to the host: Kestrel drops a HEAD body, TestServer does not.
            if (HttpMethods.IsHead(Request.Method)) return;

            await Response.Body.WriteAsync(bytes, cancellationToken);
        });

    /// <summary>
    /// Generic WebDAV clients GET the collection. Without this the card route's <c>{*davName}</c>
    /// would answer a routing 404 on a URL that does not present that segment; a 405 naming the
    /// verbs is an answer every client knows how to file.
    /// </summary>
    [HttpGet(CollectionRoute)]
    [HttpHead(CollectionRoute)]
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

    /// <summary>
    /// PUT — create or replace one card. The preconditions are evaluated FIRST (RFC 7232 puts them
    /// before any processing of the body), and a PUT they refuse archives its body under the
    /// <c>Rejected</c> cause before the 412 leaves: the book never held that version, but the
    /// server does — the bytes are read and bounded — and throwing them away is a decision, not a
    /// fatality, when DAVx5 applies "the server wins" without consulting anyone. Only a body that
    /// decodes as strict UTF-8 is archived: the storage is text, and what it cannot give back
    /// verbatim it must not pretend to keep.
    /// </summary>
    [HttpPut(CardRoute)]
    [RequestSizeLimit(PutBodyBytes)]
    public Task PutCardAsync(Guid userId, string? davName, CancellationToken cancellationToken) =>
        TracedAsync(userId, DavResourceKind.Card, async trace =>
        {
            if (string.IsNullOrEmpty(davName))
            {
                // UNREACHABLE, and kept: the verbless MethodNotAllowedOnCollection is bound on the
                // literal collection template, which outranks this catch-all one. It stays because
                // it is what proves davName non-null below; a `!` instead would be an NRE — a
                // 500 — the day route precedence changes.
                Response.Headers.Allow = DavHeaders.CollectionAllow;
                Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                return;
            }

            if (!DavName.IsValid(davName))
            {
                // Decision 5: a name this book will not hold is refused by a considered answer,
                // never by a routing 404, which a client reads as "this collection does not
                // contain that". What the guard buys: a literal '/' (a multi-segment path — a
                // percent-encoded %2F stays ENCODED in a catch-all value and never becomes one),
                // a backslash, control characters, edge spaces under PAD SPACE, and length.
                await RefuseAsync(trace, ValidAddressData, null, cancellationToken);
                return;
            }

            var user = AuthenticatedUser;
            var body = await ReadCardBodyAsync(cancellationToken);

            var card = await contacts.FindAsync(user.WebmailUid, davName, cancellationToken);
            var entityTag = card is null ? null : DavProperties.EntityTag(card);

            if (RefusedByPreconditions(entityTag))
            {
                await ArchiveRefusedBodyAsync(user.WebmailUid, davName, body, cancellationToken);
                Response.StatusCode = StatusCodes.Status412PreconditionFailed;
                return;
            }

            if (body is null)
            {
                await RefuseAsync(trace, ValidAddressData, null, cancellationToken);
                return;
            }

            // The header rides along: the pre-check above ran before any lock, so the decisive
            // If-Match comparison is the gate's, under the state lock.
            var outcome = await writer.PutAsync(user.WebmailUid, davName, body, cancellationToken,
                createOnly: DemandsCreation(), ifMatch: HeaderOrNull(Request.Headers.IfMatch));
            // A race's loser, refused INSIDE the gate: nothing was written, so the body genuinely
            // never reached the book and earns the same archive as any 412.
            if (outcome.Status is DavWriteStatus.AlreadyExists or DavWriteStatus.PreconditionFailed)
                await ArchiveRefusedBodyAsync(user.WebmailUid, davName, body, cancellationToken);

            trace.Condition = await AnswerPutOutcomeAsync(outcome, DemandsCreation(), cancellationToken);
        });

    /// <summary>
    /// DELETE — remove one card. The order is ownership, then the read, then the preconditions,
    /// then the removal, and the removal alone lays a tombstone. A refusal must lay NONE: a
    /// tombstone is what tells every other device the card is gone, and <c>sync-collection</c>
    /// serves it faithfully — so one laid beside a 412 erases everywhere a card the server has just
    /// said it was keeping, and the rank it consumed wakes every client for a change that never
    /// happened. No <c>[RequestSizeLimit]</c>: a DELETE carries no body and this action reads none.
    /// </summary>
    [HttpDelete(CardRoute)]
    public Task DeleteCardAsync(Guid userId, string? davName, CancellationToken cancellationToken) =>
        TracedAsync(userId, DavResourceKind.Card, async trace =>
        {
            if (string.IsNullOrEmpty(davName))
            {
                // UNREACHABLE for the same reason as PUT's, and kept for the same one.
                Response.Headers.Allow = DavHeaders.CollectionAllow;
                Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                return;
            }

            // A name the book will not hold designates nothing, so it is the same 404 an unknown
            // name gets — never PUT's 403, which answers "that name will not do" about a card this
            // request is not bringing. The reader's visibility clause makes a pre-backfill row that
            // same absence: what the protocol never served, it cannot be asked to delete.
            if (await FindCardOr404Async(davName, cancellationToken) is not { } card) return;

            if (RefusedByPreconditions(DavProperties.EntityTag(card)))
            {
                Response.StatusCode = StatusCodes.Status412PreconditionFailed;
                return;
            }

            // The header rides along, as on PUT: the check above is the fast path, the gate's
            // re-comparison under the state lock is the decision. Deleted is 204, the row that
            // vanished between the read and the write the same 404 an absent name answers, and a
            // lost lock race the 503 that dates its own retry.
            trace.Condition = await AnswerOutcomeAsync(
                await writer.DeleteAsync(AuthenticatedUser.WebmailUid, davName, cancellationToken,
                    HeaderOrNull(Request.Headers.IfMatch)),
                cancellationToken);
        });

    /// <summary>
    /// DELETE — the only book cannot go away, so deleting it EMPTIES it (4d decision 3): every
    /// served card archived and buried in the store's batches, the collection immediately answering
    /// again, empty. RFC 4918 § 9.6 minus one nuance the RFC does not forbid: the collection
    /// reappears at once. This is the tester's model (DELETE then PUT into it) and DAVx5's
    /// "Delete collection" gesture. No If-Match: the collection has no ETag to compare.
    /// </summary>
    [HttpDelete(CollectionRoute)]
    public Task DeleteCollectionAsync(Guid userId, CancellationToken cancellationToken) =>
        TracedAsync(userId, DavResourceKind.Collection, async trace =>
        {
            trace.Condition = await AnswerOutcomeAsync(
                await writer.DeleteAllAsync(AuthenticatedUser.WebmailUid, cancellationToken),
                cancellationToken);
        });

    private Task ProppatchAsync(DavResourceKind kind, Guid? userId, string? davName,
        string? rootHref, CancellationToken cancellationToken) =>
        TracedAsync(userId, kind, async trace =>
        {
            IReadOnlyList<XName> names;
            try
            {
                names = DavPropertyUpdate.NamesIn(
                    await DavXmlReader.ParseAsync(Request.Body, cancellationToken, logger));
            }
            catch (DavBadRequestException ex)
            {
                BodyRefused(ex);
                return;
            }

            // Answering 207 on a name that designates nothing would tell the client the card
            // exists — the same lie PROPFIND and GET refuse to tell here.
            DavCard? card = null;
            if (kind is DavResourceKind.Card && (card = await FindCardOr404Async(davName, cancellationToken)) is null)
                return;

            await using var writer = await MultiStatusWriter.BeginAsync(Response, cancellationToken);
            await writer.WriteRefusalAsync(
                HrefOf(kind, AuthenticatedUser.WebmailUid, card?.DavName, rootHref), names, cancellationToken);
            trace.Responses = writer.ResponseCount;
        });

    private Task ReportAsync(DavResourceKind kind, Guid? userId, string? davName,
        string? rootHref, CancellationToken cancellationToken) =>
        TracedAsync(userId, kind, async trace =>
        {
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
                BodyRefused(ex);
                return;
            }

            // The name the client wrote, not the kind we recognised: a report we do not serve is
            // exactly the case the line has to name, and "Unknown" would erase which one it was.
            trace.Report = document?.Root?.Name.LocalName;

            // The Depth header is deliberately ignored, never refused: PROPFIND's rule is PROPFIND's
            // alone — a report already says what it applies to, so there is nothing to guess.
            var user = AuthenticatedUser;
            var requestHref = HrefOf(kind, user.WebmailUid, davName, rootHref);

            try
            {
                // Each report is gated by the shapes whose supported-report-set announces it —
                // RFC 6352 § 8.6 and § 8.7 for the book and an address resource, RFC 3253 § 3.8 for
                // everything but a card (no property of a card is href-valued), RFC 6578 § 3.1 for
                // the collection alone. Served off those shapes, a report would answer where the
                // resource never claimed it — the mirror of the announcement that made DAVx5 loop.
                switch (document is null ? DavReportKind.Unknown : ReportRequest.KindOf(document))
                {
                    case DavReportKind.Multiget
                        when kind is DavResourceKind.Collection or DavResourceKind.Card:
                        trace.Responses = await MultigetReport.WriteAsync(Response, document!, requestHref,
                            user.WebmailUid, user.Email, contacts, cancellationToken);
                        return;
                    case DavReportKind.ExpandProperty when kind is not DavResourceKind.Card:
                        trace.Responses = await ExpandAsync(kind, document!, requestHref, cancellationToken);
                        return;
                    case DavReportKind.Query
                        when kind is DavResourceKind.Collection or DavResourceKind.Card:
                        trace.Responses = await QueryAsync(kind, davName, document!, requestHref,
                            cancellationToken);
                        return;
                    case DavReportKind.SyncCollection when kind is DavResourceKind.Collection:
                        // tokenIn BEFORE the call: the refusal path is the one the field exists
                        // for — "a token refused in a loop" is separable from the four other
                        // empty-book causes only by reading WHICH token looped.
                        trace.TokenIn = DavSyncToken.ForLog(
                            document!.Root!.Element(DavXml.Dav + "sync-token")?.Value);
                        var sync = await SyncCollectionReport.WriteAsync(Response, document,
                            requestHref, user.WebmailUid, user.Email, DepthHeader(), contacts,
                            syncStore, preferences, cancellationToken);
                        trace.Responses = sync.Responses;
                        trace.TokenOut = DavSyncToken.ForLog(sync.TokenOut);
                        return;
                    default:
                        // Unknown, or asked off the shape that defines it: a report we do not
                        // serve is a considered 403 — a 500 makes a client loop on it forever.
                        await RefuseAsync(trace, SupportedReport, null, cancellationToken);
                        return;
                }
            }
            catch (DavPreconditionException ex)
            {
                await RefuseAsync(trace, ex.Condition, ex.Detail, cancellationToken);
            }
            catch (DavBadRequestException ex)
            {
                // Thrown by a report reader on a body the XML parser could not judge — an
                // expand-property name no element can carry. Always before the multistatus opens.
                BodyRefused(ex);
            }
        });

    /// <summary>
    /// A query on a card is scoped to that card alone, and a name the book no longer holds is a
    /// 404 on the resource itself — not an empty multistatus, which would say the card exists and
    /// matches nothing.
    /// </summary>
    private async Task<int> QueryAsync(DavResourceKind kind, string? davName, XDocument document,
        string requestHref, CancellationToken cancellationToken)
    {
        DavCard? card = null;
        if (kind is DavResourceKind.Card && (card = await FindCardOr404Async(davName, cancellationToken)) is null)
            return 0;

        var user = AuthenticatedUser;
        return await AddressBookQueryReport.WriteAsync(Response, document, requestHref,
            user.WebmailUid, user.Email, card, contacts, cancellationToken);
    }

    /// <summary>The card kind never reaches here: the switch above gates it out, because no
    /// property of a card is href-valued and none of the cards announces this report.</summary>
    private async Task<int> ExpandAsync(DavResourceKind kind, XDocument document,
        string requestHref, CancellationToken cancellationToken)
    {
        var user = AuthenticatedUser;
        var state = kind is DavResourceKind.Collection
            ? await syncStore.ReadStateAsync(user.WebmailUid, cancellationToken)
            : null;
        var target = new DavResourceContext(kind, user.WebmailUid, user.Email, null, state);
        return await ExpandPropertyReport.WriteAsync(Response, document, target, requestHref,
            resource => NestedContext(resource, user.WebmailUid, user.Email), cancellationToken);
    }

    /// <summary>
    /// The context a nested expand-property target resolves against, or null for anything that is
    /// not this user's — the nested 404. The resolution is synchronous, so no context built here
    /// can carry a card or a sync state; the two kinds that would need one are refused rather than
    /// answered without it. No href property of ours designates either today, so the refusal is
    /// unreachable — and the day one does, a nested 404 sends the client back to a PROPFIND,
    /// where a ctag of "0" and a token on the empty epoch would have been believed instead.
    /// </summary>
    private static DavResourceContext? NestedContext(DavResource resource, Guid userId, string email)
    {
        if (resource.Kind is DavResourceKind.ServiceRoot)
            return new DavResourceContext(DavResourceKind.ServiceRoot, userId, email, null, null);
        if (resource.Kind is DavResourceKind.Card or DavResourceKind.Collection
            || resource.UserId != userId)
        {
            return null;
        }

        return new DavResourceContext(resource.Kind, userId, email, null, null);
    }

    private Task PropfindAsync(DavResourceKind kind, Guid? userId, string? davName,
        string? rootHref, CancellationToken cancellationToken) =>
        TracedAsync(userId, kind, async trace =>
        {
            var depth = DavDepth.Parse(DepthHeader());
            if (depth is null)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (depth is DavDepthValue.Infinity)
            {
                // RFC 4918 § 9.1 reserves the refusal to collections; on anything else infinity
                // IS depth 0, and refusing it fails a PROPFIND on a card for a header it never needed.
                if (kind is DavResourceKind.Home or DavResourceKind.Collection)
                {
                    await RefuseAsync(trace, FiniteDepth, null, cancellationToken);
                    return;
                }

                depth = DavDepthValue.Zero;
            }

            DavPropertyRequest request;
            try
            {
                request = DavPropertyRequest.Parse(
                    await DavXmlReader.ParseAsync(Request.Body, cancellationToken, logger));
            }
            catch (DavBadRequestException ex)
            {
                BodyRefused(ex);
                return;
            }

            DavCard? card = null;
            if (kind is DavResourceKind.Card && (card = await FindCardOr404Async(davName, cancellationToken)) is null)
                return;

            var user = AuthenticatedUser;

            async Task WriteAsync(CancellationToken token)
            {
                // The counter BEFORE the members, and this is an order, not a preference: the
                // fallback path without sync-collection holds the ctag it reads as covering the
                // member list it reads next. Read the other way round, a write committing in
                // between is covered by the returned ctag without appearing in the list — the
                // client believes it seen and never asks again.
                var state = NeedsState(kind, depth.Value)
                    ? await syncStore.ReadStateAsync(user.WebmailUid, token)
                    : null;

                var resource = new DavResourceContext(kind, user.WebmailUid, user.Email, card, state);
                var href = HrefOf(kind, user.WebmailUid, card?.DavName, rootHref);

                await using var writer = await MultiStatusWriter.BeginAsync(Response, token);
                try
                {
                    await WriteResourceAsync(writer, href, request, resource, token);

                    if (depth is DavDepthValue.One && kind is DavResourceKind.Home)
                    {
                        await WriteResourceAsync(writer, DavPaths.Collection(user.WebmailUid), request,
                            resource with { Kind = DavResourceKind.Collection }, token);
                    }

                    if (depth is DavDepthValue.One && kind is DavResourceKind.Collection)
                    {
                        await foreach (var member in
                            contacts.StreamAsync(user.WebmailUid, MemberBound(state), token))
                        {
                            await WriteResourceAsync(writer, DavPaths.Card(user.WebmailUid, member.DavName),
                                request, resource with { Kind = DavResourceKind.Card, Card = member },
                                token);
                        }
                    }
                }
                finally
                {
                    // Read off the writer: a book that streamed halfway before the connection died
                    // still says how far it got, which is the whole point of the line.
                    trace.Responses = writer.ResponseCount;
                }
            }

            await InOneSnapshotAsync(kind, depth.Value, WriteAsync, cancellationToken);
        });

    /// <summary>
    /// The frame every action runs in. Ownership first — a foreign <c>{userId}</c> answers 404,
    /// never 403, which would confirm the principal exists — then the canonical slash, then the
    /// action; and whichever way it leaves, the one log line of decision 18, with the status the
    /// host will write when an exception is on its way out.
    /// </summary>
    private async Task TracedAsync(Guid? userId, DavResourceKind kind, Func<Trace, Task> action)
    {
        var trace = new Trace();
        int? status = null;
        try
        {
            if (userId is { } target && target != AuthenticatedUser.WebmailUid)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (userId is { } owner && RedirectedToCanonical(kind, owner)) return;

            await action(trace);
        }
        catch (Exception exception)
        {
            status = StatusWrittenAfter(exception);
            throw;
        }
        finally
        {
            LogRequest(trace, status);
        }
    }

    /// <summary>
    /// The card, or the 404 an unknown name gets — an invalid name too: a literal '/' or '\', a
    /// control character, an edge space designate nothing (a percent-encoded %2F stays ENCODED in
    /// a catch-all value and never becomes one). Never a 400: 404 is what a client files.
    /// </summary>
    private async Task<DavCard?> FindCardOr404Async(string? davName, CancellationToken cancellationToken)
    {
        var card = DavName.IsValid(davName)
            ? await contacts.FindAsync(AuthenticatedUser.WebmailUid, davName!, cancellationToken)
            : null;
        if (card is null) Response.StatusCode = StatusCodes.Status404NotFound;
        return card;
    }

    /// <summary>A considered 403 carrying its precondition, and the log line's condition with it.</summary>
    private Task RefuseAsync(Trace trace, XName condition, XElement? detail, CancellationToken cancellationToken)
    {
        trace.Condition = condition.LocalName;
        return DavError.WriteAsync(Response, StatusCodes.Status403Forbidden, condition, detail,
            cancellationToken, logger);
    }

    /// <summary>The 400 of a body the reader could not judge — never a 500, which a client retries
    /// for ever. A <see cref="BadHttpRequestException"/> never lands here: Kestrel's 413, not our 400.</summary>
    private void BodyRefused(DavBadRequestException ex)
    {
        logger.LogInformation("{Method} body refused: {Reason}", Request.Method, ex.Message);
        if (!Response.HasStarted) Response.StatusCode = StatusCodes.Status400BadRequest;
    }

    /// <summary>
    /// Null means the body is not strict UTF-8 — <see cref="DavBody.TryDecode"/> refuses rather
    /// than replaces, or the ETag would describe bytes other than the sent ones. The read is
    /// bounded by <c>[RequestSizeLimit]</c>, whose 413 flies through as a
    /// <see cref="BadHttpRequestException"/>: bytes the server refuses to hold cannot be archived either.
    /// </summary>
    private async Task<string?> ReadCardBodyAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        return DavBody.TryDecode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), out var text)
            ? text
            : null;
    }

    /// <summary>
    /// RFC 7232 § 6: If-Match first, with the STRONG comparison — a weak tag says "semantically
    /// equivalent", no promise a byte-for-byte replacement can rest on — and an ABSENT If-Match is
    /// not a failed precondition. If-None-Match then refuses what exists, <c>*</c> above all: the
    /// "create only" a client spells when it holds no copy whose loss it could tolerate. Shared
    /// with DELETE, where RFC 9110 § 13.1.2 asks for exactly the same two evaluations.
    /// </summary>
    private bool RefusedByPreconditions(string? entityTag)
    {
        var ifMatch = HeaderOrNull(Request.Headers.IfMatch);
        if (ifMatch is not null && (entityTag is null || !EntityTagMatcher.Match(ifMatch, entityTag)))
            return true;

        var ifNoneMatch = HeaderOrNull(Request.Headers.IfNoneMatch);
        return ifNoneMatch is not null && entityTag is not null
            && EntityTagMatcher.NoneMatch(ifNoneMatch, entityTag);
    }

    /// <summary>
    /// Archives a refused body before its 412 leaves — only when it decodes: the storage is text,
    /// and what it cannot give back verbatim it must not pretend to keep.
    /// </summary>
    private async Task ArchiveRefusedBodyAsync(
        Guid userId, string davName, string? body, CancellationToken cancellationToken)
    {
        if (body is null) return;
        if (!await writer.ArchiveRejectedAsync(userId, davName, body, cancellationToken))
            logger.LogInformation("The refused PUT body for {DavName} was not archived", davName);
    }

    /// <summary>True when If-None-Match spells <c>*</c> — the request only consents to create.</summary>
    private bool DemandsCreation() =>
        HeaderOrNull(Request.Headers.IfNoneMatch) is { } header
        && header.Split(',').Any(member => member.Trim() == "*");

    private static string? HeaderOrNull(StringValues values) =>
        StringValues.IsNullOrEmpty(values) ? null : values.ToString();

    /// <summary>
    /// Writes the response a write outcome calls for and answers the condition it named, for the
    /// log line. Each refusal keeps its OWN precondition element: a client abandons a
    /// valid-address-data, re-exports a supported-address-data, and re-reads the href a
    /// no-uid-conflict carries — collapsing them would erase what to do next.
    /// </summary>
    private Task<string?> AnswerPutOutcomeAsync(DavWriteOutcome outcome, bool mustCreate,
        CancellationToken cancellationToken)
    {
        if (mustCreate && outcome.Status is DavWriteStatus.Replaced)
        {
            // The net beneath the gate's own createOnly refusal: a Replaced that reaches here
            // under If-None-Match: * means a write the gate should have refused — answer the
            // 412 the condition earns rather than a 204 that says the create happened.
            Response.StatusCode = StatusCodes.Status412PreconditionFailed;
            return Task.FromResult<string?>(null);
        }

        return AnswerOutcomeAsync(outcome, cancellationToken);
    }

    /// <summary>
    /// Hands the outcome to the one translator every write answer goes through, and gives back the
    /// condition it named for the log line. Nothing here decides a status: a second mapping beside
    /// <see cref="DavOutcomeTranslator"/> is exactly how a branch ends up missing and a client ends
    /// up retrying a 500 on the same card for ever.
    /// </summary>
    private async Task<string?> AnswerOutcomeAsync(
        DavWriteOutcome outcome, CancellationToken cancellationToken)
    {
        await DavOutcomeTranslator.WriteAsync(Response, outcome, cancellationToken, logger);
        return DavOutcomeTranslator.ConditionOf(outcome.Status)?.LocalName;
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
    /// The one line this request leaves, on every path out of an action — the error paths above
    /// all, since a failure of this protocol reaches the user as a book that is simply empty, with
    /// nothing on the server saying which of its five causes it was. The path, never the query:
    /// the query is where a token travels.
    /// </summary>
    /// <param name="trace">what the action accumulated; null on the verbless answers</param>
    /// <param name="status">
    /// The status the host will write once this action has returned, when an exception is on its
    /// way out and that status is therefore not on the response yet. Null everywhere else, where
    /// <see cref="HttpResponse.StatusCode"/> is already the answer.
    /// </param>
    private void LogRequest(Trace? trace = null, int? status = null) =>
        DavRequestLog.Write(logger, new DavRequestTrace(
            Request.Method, Request.Path.Value ?? string.Empty, DepthHeader(), trace?.Report,
            trace?.TokenIn, trace?.TokenOut, trace?.Responses ?? 0, status ?? Response.StatusCode,
            trace?.Condition));

    /// <summary>
    /// The status the host writes for an exception leaving an action, or null when it writes none.
    /// This has to be read in a <c>catch</c> and cannot be read in the <c>finally</c>: Kestrel sets
    /// the 413 of a body past <c>[RequestSizeLimit]</c> AFTER the action returns, so a line reading
    /// <see cref="HttpResponse.StatusCode"/> there reports the untouched 200 the response has not
    /// yet stopped carrying. A cancellation is the one case that answers null: the client is gone,
    /// and whatever the response already carries is the truthful line.
    /// </summary>
    private static int? StatusWrittenAfter(Exception exception) => exception switch
    {
        OperationCanceledException => null,
        BadHttpRequestException refused => refused.StatusCode,
        _ => StatusCodes.Status500InternalServerError,
    };

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
    /// The rank the members are bounded to — the counter this answer's ctag is cut from, so the
    /// two halves say the same thing. No state row bounds nothing: the ctag then renders the
    /// sentinel <c>"0"</c> no live book emits, so there is no claim for the bound to keep honest,
    /// while bounding at 0 would answer an empty book — which a client applies by deleting its
    /// copies.
    /// </summary>
    private static ulong MemberBound(SyncState? state) => state?.Seq ?? ulong.MaxValue;

    /// <summary>
    /// One snapshot over the counter and the members, and ONLY where both are read: everywhere
    /// else a PROPFIND is a single statement and a transaction would buy nothing.
    /// </summary>
    /// <remarks>
    /// The bound is free inside one snapshot and costly outside it. An edit gives a card a NEW,
    /// higher rank (ContactStore.UpdateAsync), so a webmail edit landing between the counter read
    /// and the member query moves a card above the bound: the list loses it while the ctag still
    /// covers its old rank, and a client reads absence from a Depth: 1 list as a server-side delete
    /// and removes its copy — restored only at the next ctag poll, hours later by DAVx5's default.
    /// On MySQL's REPEATABLE READ the first SELECT pins the snapshot both reads then share, which
    /// is what makes "every card satisfies sync_sequence &lt;= seq" true rather than merely likely.
    /// Opened through the execution strategy the way SyncCollectionReport.WriteAsync is; the
    /// snapshot stays open while the members stream, which a PROPFIND never fills with
    /// address-data and the book caps at 5000 rows.
    /// <para>
    /// Unlike sync-collection, this one CANNOT lift its read out of the transaction: what that
    /// report's snapshot protects is a pair of reads over tombstones, while this one's protects the
    /// counter against the member query itself — the very read that streams. So the member loop
    /// deliberately does NOT call <c>MultiStatusWriter.FlushIfDueAsync</c>, where the two streaming
    /// reports do: flushing hands the pace of the read to whoever drains the socket, and here that
    /// would make a slow client the reason an InnoDB read view stays open. Left unflushed, the
    /// document reaches the wire only as the XML writer's own buffer fills, so the response is
    /// paced by this process and not by the client. Taking the cards out of the snapshot would
    /// need a member projection carrying a byte count instead of vcard_raw — the properties this
    /// answer serves need the length, never the card — which is a change to DavCard,
    /// IDavContactReader and the property tables, not to this method.
    /// </para>
    /// </remarks>
    private Task InOneSnapshotAsync(DavResourceKind kind, DavDepthValue depth,
        Func<CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        if (kind is not DavResourceKind.Collection || depth is not DavDepthValue.One)
            return write(cancellationToken);

        // Result-typed although nothing is wanted back: the token-taking ExecuteAsync has no void
        // form, and an async lambda passed inline leaves its generic result unresolved.
        Func<CancellationToken, Task<bool>> operation = async token =>
        {
            await using var transaction = await preferences.Database.BeginTransactionAsync(token);
            await write(token);
            await transaction.CommitAsync(token);
            return true;
        };
        return preferences.Database.CreateExecutionStrategy()
            .ExecuteAsync(operation, cancellationToken);
    }

    /// <summary>
    /// Capabilities, answered off the URL shape alone: <c>[AllowAnonymous]</c> on the three OPTIONS
    /// actions and on no other action, because a client asks what the server can do before it holds
    /// any credentials — which is also why it consults no store and reveals nothing a URL did not
    /// already carry.
    /// </summary>
    [AcceptVerbs("OPTIONS", Route = "")]
    [AcceptVerbs("OPTIONS", Route = "/")]
    [AcceptVerbs("OPTIONS", Route = "principals/{userId:guid}")]
    [AcceptVerbs("OPTIONS", Route = "addressbooks/{userId:guid}")]
    [AllowAnonymous]
    public void OptionsHome() => Capabilities(DavHeaders.HomeAllow);

    [AcceptVerbs("OPTIONS", Route = CollectionRoute)]
    [AllowAnonymous]
    public void OptionsCollection() => Capabilities(DavHeaders.CollectionAllow);

    [AcceptVerbs("OPTIONS", Route = CardRoute)]
    [AllowAnonymous]
    public void OptionsCard() => Capabilities(DavHeaders.CardAllow);

    /// <summary>
    /// Last on purpose, and bound to no verb: carrying no method metadata, these three actions —
    /// this one and its siblings <see cref="MethodNotAllowedOnCollection"/> and
    /// <see cref="MethodNotAllowedOnCard"/> — score below every real route above, so action
    /// selection reaches them only when nothing else answers the verb. They carry <c>Allow</c> and
    /// nothing else. Routing supplies an Allow of its own, but it is the union of the verbs bound on
    /// the template: on the collection and card catch-alls it names GET and HEAD, which answer 405
    /// on a collection, and omits PUT and DELETE, which a card announces — either way a client that
    /// reads it is told something the surface does not do.
    /// </summary>
    [Route("")]
    [Route("principals/{userId:guid}")]
    [Route("addressbooks/{userId:guid}")]
    public void MethodNotAllowedOnHome() => MethodNotAllowed(DavHeaders.HomeAllow);

    [Route(CollectionRoute)]
    public void MethodNotAllowedOnCollection() => MethodNotAllowed(DavHeaders.CollectionAllow);

    [Route(CardRoute)]
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

    /// <summary>What one request's log line accumulates on its way through an action.</summary>
    private sealed class Trace
    {
        public string? Report { get; set; }
        public int Responses { get; set; }
        public string? Condition { get; set; }
        public string? TokenIn { get; set; }
        public string? TokenOut { get; set; }
    }
}
