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
public sealed class CardDavController(
    IDavContactReader contacts,
    IDavContactWriter writer,
    IContactSyncStore syncStore,
    PreferencesDbContext preferences,
    ILogger<CardDavController> logger) : ApiBaseController
{
    private const int MaxBodyBytes = 1024 * 1024;

    private static readonly XName FiniteDepth = DavXml.Dav + "propfind-finite-depth";
    private static readonly XName SupportedReport = DavXml.Dav + "supported-report";
    private static readonly XName ValidAddressData = DavXml.CardDav + "valid-address-data";

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
        int? status = null;
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
        catch (Exception exception)
        {
            status = StatusWrittenAfter(exception);
            throw;
        }
        finally
        {
            LogRequest(responses: responses, status: status);
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
        int? status = null;
        try
        {
            var user = AuthenticatedUser;
            if (userId != user.WebmailUid)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // Ruling AM, corrected by task 7: a catch-all keeps %2F ENCODED, so the '/' IsValid
            // refuses only ever arrives literally (a multi-segment path) or as '\'. An invalid
            // name designates nothing — the same 404 an unknown name gets, never a 400.
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
        catch (Exception exception)
        {
            status = StatusWrittenAfter(exception);
            throw;
        }
        finally
        {
            LogRequest(responses: served, status: status);
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

    /// <summary>
    /// PUT — create or replace one card. The preconditions are evaluated FIRST (RFC 7232 puts them
    /// before any processing of the body), and a PUT they refuse archives its body under the
    /// <c>Rejected</c> cause before the 412 leaves: the book never held that version, but the
    /// server does — the bytes are read and bounded — and throwing them away is a decision, not a
    /// fatality, when DAVx5 applies "the server wins" without consulting anyone. Only a body that
    /// decodes as strict UTF-8 is archived: the storage is text, and what it cannot give back
    /// verbatim it must not pretend to keep.
    /// </summary>
    [HttpPut("addressbooks/{userId:guid}/" + DavPaths.BookName + "/{*davName}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task PutCardAsync(Guid userId, string? davName, CancellationToken cancellationToken)
    {
        string? condition = null;
        int? status = null;
        try
        {
            var user = AuthenticatedUser;
            if (userId != user.WebmailUid)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (string.IsNullOrEmpty(davName))
            {
                // UNREACHABLE, and kept: the verbless MethodNotAllowedOnCollection is bound on the
                // literal collection template, which outranks this catch-all one, so it answers
                // the collection URL first — mutating this branch kills no test. It stays because
                // it is what proves davName non-null below; deleting it would need a `!`, turning
                // dead-but-safe code into an NRE — a 500 — the day route precedence changes.
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
                condition = ValidAddressData.LocalName;
                await DavError.WriteAsync(Response, StatusCodes.Status403Forbidden, ValidAddressData,
                    cancellationToken: cancellationToken, logger: logger);
                return;
            }

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
                condition = ValidAddressData.LocalName;
                await DavError.WriteAsync(Response, StatusCodes.Status403Forbidden, ValidAddressData,
                    cancellationToken: cancellationToken, logger: logger);
                return;
            }

            // The header rides along: the pre-check above ran before any lock, so the decisive
            // If-Match comparison is the gate's, under the state lock — ruling BO's seam, closed
            // for the replacement case as it was for creation.
            var outcome = await writer.PutAsync(user.WebmailUid, davName, body, cancellationToken,
                createOnly: DemandsCreation(), ifMatch: HeaderOrNull(Request.Headers.IfMatch));
            // A race's loser, refused INSIDE the gate: nothing was written, so the body genuinely
            // never reached the book and earns the same archive as any 412.
            if (outcome.Status is DavWriteStatus.AlreadyExists or DavWriteStatus.PreconditionFailed)
                await ArchiveRefusedBodyAsync(user.WebmailUid, davName, body, cancellationToken);

            condition = await AnswerPutOutcomeAsync(outcome, DemandsCreation(), cancellationToken);
        }
        catch (Exception exception)
        {
            status = StatusWrittenAfter(exception);
            throw;
        }
        finally
        {
            LogRequest(condition: condition, status: status);
        }
    }

    /// <summary>
    /// DELETE — remove one card. The order is ownership, then the read, then the preconditions,
    /// then the removal, and the removal alone lays a tombstone. A refusal must lay NONE: a
    /// tombstone is what tells every other device the card is gone, and <c>sync-collection</c>
    /// serves it faithfully — so one laid beside a 412 erases everywhere a card the server has just
    /// said it was keeping, and the rank it consumed wakes every client for a change that never
    /// happened. No <c>[RequestSizeLimit]</c>: a DELETE carries no body, this action reads none,
    /// and the attribute's limit only ever trips on a read.
    /// </summary>
    [HttpDelete("addressbooks/{userId:guid}/" + DavPaths.BookName + "/{*davName}")]
    public async Task DeleteCardAsync(Guid userId, string? davName, CancellationToken cancellationToken)
    {
        string? condition = null;
        int? status = null;
        try
        {
            var user = AuthenticatedUser;
            if (userId != user.WebmailUid)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (string.IsNullOrEmpty(davName))
            {
                // UNREACHABLE for the same reason as PUT's, and kept for the same one: routing's
                // verbless catch-all answers the collection URL, and this branch is what proves
                // davName non-null below. Deleting the whole book is a gesture the product offers
                // nowhere, so the answer it would give stays the catch-all's 405.
                Response.Headers.Allow = DavHeaders.CollectionAllow;
                Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                return;
            }

            // A name the book will not hold designates nothing, so it is the same 404 an unknown
            // name gets — never PUT's 403, which answers "that name will not do" about a card this
            // request is not bringing. The reader's visibility clause makes a pre-backfill row that
            // same absence: what the protocol never served, it cannot be asked to delete.
            var card = DavName.IsValid(davName)
                ? await contacts.FindAsync(user.WebmailUid, davName, cancellationToken)
                : null;
            if (card is null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (RefusedByPreconditions(DavProperties.EntityTag(card)))
            {
                Response.StatusCode = StatusCodes.Status412PreconditionFailed;
                return;
            }

            // The header rides along, as on PUT: the check above is the fast path, the gate's
            // re-comparison under the state lock is the decision.
            condition = await AnswerDeleteOutcomeAsync(
                await writer.DeleteAsync(user.WebmailUid, davName, cancellationToken,
                    HeaderOrNull(Request.Headers.IfMatch)),
                cancellationToken);
        }
        catch (Exception exception)
        {
            status = StatusWrittenAfter(exception);
            throw;
        }
        finally
        {
            LogRequest(condition: condition, status: status);
        }
    }

    /// <summary>
    /// Deleted is 204, the row that vanished between the read and the write is the same 404 an
    /// absent name answers, and a lost lock race is the 503 that dates its own retry. No card is
    /// read here so no card refusal can come back — and the ones that cannot still get their own
    /// named answer rather than the 500 a throw would hand a client that then retries for ever.
    /// </summary>
    private Task<string?> AnswerDeleteOutcomeAsync(
        DavWriteOutcome outcome, CancellationToken cancellationToken) =>
        AnswerOutcomeAsync(outcome, cancellationToken);

    /// <summary>
    /// Null means the body is not strict UTF-8 — <see cref="DavBody.TryDecode"/> refuses rather
    /// than replaces, or the ETag would describe bytes other than the sent ones. The read is
    /// bounded by <c>[RequestSizeLimit]</c>, whose 413 flies through as a
    /// <see cref="BadHttpRequestException"/>: Kestrel's answer, not ours, and bytes the server
    /// refuses to hold cannot be archived either.
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

    /// <summary>REPORT is bound on every shape whose <c>Allow</c> names it — the home and the
    /// service root included, where a 405 under our own header would make an RFC 9110 client
    /// retry the verb for ever. The default branch's <c>403 supported-report</c> is the
    /// considered answer there, and expand-property genuinely serves the root's principal.
    /// The bare root stays unbound on purpose: no catch-all of ours answers there, so its 405
    /// carries routing's own Allow, which honestly omits REPORT — no header lies.</summary>
    [AcceptVerbs("REPORT", Route = "")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task ReportServiceRootAsync(CancellationToken cancellationToken) =>
        ReportAsync(DavResourceKind.ServiceRoot, null, null, DavPaths.Root + "/", cancellationToken);

    [AcceptVerbs("REPORT", Route = "principals/{userId:guid}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task ReportPrincipalAsync(Guid userId, CancellationToken cancellationToken) =>
        ReportAsync(DavResourceKind.Principal, userId, null, null, cancellationToken);

    [AcceptVerbs("REPORT", Route = "addressbooks/{userId:guid}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task ReportHomeAsync(Guid userId, CancellationToken cancellationToken) =>
        ReportAsync(DavResourceKind.Home, userId, null, null, cancellationToken);

    [AcceptVerbs("REPORT", Route = "addressbooks/{userId:guid}/" + DavPaths.BookName)]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task ReportCollectionAsync(Guid userId, CancellationToken cancellationToken) =>
        ReportAsync(DavResourceKind.Collection, userId, null, null, cancellationToken);

    /// <summary>RFC 6352 § 8.7 defines multiget on address resources too, and
    /// supported-report-set says so on every card — without this route the header lies.</summary>
    [AcceptVerbs("REPORT", Route = "addressbooks/{userId:guid}/" + DavPaths.BookName + "/{*davName}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task ReportCardAsync(Guid userId, string? davName, CancellationToken cancellationToken) =>
        ReportAsync(DavResourceKind.Card, userId, davName, null, cancellationToken);

    private async Task ReportAsync(DavResourceKind kind, Guid? userId, string? davName,
        string? rootHref, CancellationToken cancellationToken)
    {
        string? report = null;
        string? condition = null;
        string? tokenIn = null;
        string? tokenOut = null;
        var responses = 0;
        int? status = null;
        try
        {
            var user = AuthenticatedUser;
            if (userId is { } target && target != user.WebmailUid)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (userId is { } owner && RedirectedToCanonical(kind, owner)) return;

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
            var requestHref = HrefOf(kind, user.WebmailUid, davName, rootHref);

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
                    case DavReportKind.Query
                        when kind is DavResourceKind.Collection or DavResourceKind.Card:
                        // RFC 6352 § 8.6 defines the query on the book and on an address resource,
                        // which is exactly where supported-report-set announces it; anywhere else
                        // the guard falls through to the considered refusal below.
                        responses = await QueryAsync(kind, davName, document!, requestHref,
                            cancellationToken);
                        return;
                    case DavReportKind.SyncCollection when kind is DavResourceKind.Collection:
                        // RFC 6578 § 3.1 defines the report on the collection alone; anywhere
                        // else the guard falls through to the considered refusal below.
                        // tokenIn BEFORE the call: the refusal path is the one the field exists
                        // for — "a token refused in a loop" is separable from the four other
                        // empty-book causes only by reading WHICH token looped.
                        tokenIn = DavSyncToken.ForLog(
                            document!.Root!.Element(DavXml.Dav + "sync-token")?.Value);
                        var sync = await SyncCollectionReport.WriteAsync(Response, document,
                            requestHref, user.WebmailUid, user.Email, DepthHeader(), contacts,
                            syncStore, preferences, cancellationToken);
                        responses = sync.Responses;
                        tokenOut = DavSyncToken.ForLog(sync.TokenOut);
                        return;
                    default:
                        // Unknown, and the two reports asked off the shape that defines them,
                        // through this one branch: a report we do not serve is a considered 403 —
                        // a 500 makes a client loop on it forever.
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
            catch (DavBadRequestException ex)
            {
                // Thrown by a report reader on a body the XML parser could not judge — an
                // expand-property name no element can carry. Always before the multistatus opens;
                // the guard only keeps a future mid-write throw from turning into the 500 above.
                logger.LogInformation("REPORT body refused: {Reason}", ex.Message);
                if (!Response.HasStarted) Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        }
        catch (Exception exception)
        {
            status = StatusWrittenAfter(exception);
            throw;
        }
        finally
        {
            LogRequest(report, responses, condition, status, tokenIn, tokenOut);
        }
    }

    /// <summary>
    /// A query on a card is scoped to that card alone, and a name the book no longer holds is a
    /// 404 on the resource itself — not an empty multistatus, which would say the card exists and
    /// matches nothing.
    /// </summary>
    private async Task<int> QueryAsync(DavResourceKind kind, string? davName, XDocument document,
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

        return await AddressBookQueryReport.WriteAsync(Response, document, requestHref,
            user.WebmailUid, user.Email, card, contacts, cancellationToken);
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
        int? status = null;
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
                // An invalid name — a literal '/' or '\', a control character, an edge space; a
                // percent-encoded %2F stays encoded in a catch-all and never becomes one —
                // designates nothing, the same 404 an unknown name gets.
                card = DavName.IsValid(davName)
                    ? await contacts.FindAsync(user.WebmailUid, davName!, cancellationToken)
                    : null;
                if (card is null)
                {
                    Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
            }

            async Task WriteAsync(CancellationToken token)
            {
                // The counter BEFORE the members, and this is an order, not a preference: the
                // fallback path without sync-collection holds the ctag it reads as covering the
                // member list it reads next. Read the other way round, a write committing in
                // between is covered by the returned ctag without appearing in the list — the
                // client believes it seen and never asks again. One read serves both halves of the
                // answer; a second would contradict it.
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
                    // Read off the writer, not counted here: a book that streamed halfway before
                    // the connection died still says how far it got, which is the whole point of
                    // the line.
                    responses = writer.ResponseCount;
                }
            }

            await InOneSnapshotAsync(kind, depth.Value, WriteAsync, cancellationToken);
        }
        catch (Exception exception)
        {
            status = StatusWrittenAfter(exception);
            throw;
        }
        finally
        {
            LogRequest(responses: responses, condition: condition, status: status);
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
    /// from a <c>finally</c> so a throw still leaves the trace. The path, never the query: the
    /// query is where a token travels.
    /// </summary>
    /// <param name="report">the report a REPORT body named, as the client spelled it</param>
    /// <param name="responses">how many response elements the answer carried</param>
    /// <param name="tokenIn">the sync token the client presented, through
    /// <see cref="DavSyncToken.ForLog"/> — on the refusal path above all</param>
    /// <param name="tokenOut">the sync token the answer minted; a truncated answer mints the cut,
    /// not the counter, and telling the two apart is the diagnosis the field buys</param>
    /// <param name="condition">the precondition element that refused the request, when one did</param>
    /// <param name="status">
    /// The status the host will write once this action has returned, when an exception is on its
    /// way out and that status is therefore not on the response yet. Null everywhere else, where
    /// <see cref="HttpResponse.StatusCode"/> is already the answer and reading it is what keeps the
    /// line from claiming a status the response does not carry.
    /// </param>
    private void LogRequest(string? report = null, int responses = 0, string? condition = null,
        int? status = null, string? tokenIn = null, string? tokenOut = null) =>
        DavRequestLog.Write(logger, new DavRequestTrace(
            Request.Method, Request.Path.Value ?? string.Empty, DepthHeader(), report,
            tokenIn, tokenOut, responses, status ?? Response.StatusCode, condition));

    /// <summary>
    /// The status the host writes for an exception leaving an action, or null when it writes none.
    /// This has to be read in a <c>catch</c> and cannot be read in the <c>finally</c>: Kestrel sets
    /// the 413 of a body past <c>[RequestSizeLimit]</c> AFTER the action returns, so a line reading
    /// <see cref="HttpResponse.StatusCode"/> there reports the untouched 200 the response has not
    /// yet stopped carrying — an operator diagnosing an empty book would read a success.
    /// A cancellation is the one case that answers null: the client is gone, nothing further is
    /// written, and whatever the response already carries (a 207 whose stream died halfway, with
    /// <c>responses</c> saying how far it got) is the truthful line.
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
