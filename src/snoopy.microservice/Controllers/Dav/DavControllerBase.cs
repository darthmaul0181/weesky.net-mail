using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using weesky.Snoopy.Microservice.Authentication;
using weesky.Snoopy.Microservice.Authentication.Dav;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Controllers.Dav;

/// <summary>
/// The frame every /dav controller runs in — the reading verbs, the refusals, the one log line —
/// with the protocol left to the hooks. Deliberately NOT <c>[ApiController]</c>: its binding
/// conventions and its automatic 400 on an invalid ModelState would pre-empt this protocol's own
/// responses. The policy is named on purpose — a bare <c>[Authorize]</c> would challenge with
/// <c>WWW-Authenticate: Bearer</c>, which a DAV client has no token for and no way to ask for one.
/// Hidden from the API explorer: Swashbuckle has no OpenAPI operation type for a PROPFIND and
/// throws at scan time. The four attributes live HERE and nowhere else: MVC reads a type's
/// attributes with <c>inherit: true</c>, so a <c>[Route("dav")]</c> re-posed on a derived
/// controller would give every action two identical templates — two endpoints, and an
/// <see cref="Microsoft.AspNetCore.Routing.Matching.AmbiguousMatchException"/> on the first request.
/// Every member is <c>private protected</c>: a <c>protected</c> member of a public class is visible
/// outside the assembly, where the <c>internal</c> types it takes are not.
/// </summary>
[Route("dav")]
[Authorize(Policy = DavAuthenticationDefaults.PolicyName)]
[ApiExplorerSettings(IgnoreApi = true)]
[NoFormBinding]
public abstract class DavControllerBase(PreferencesDbContext preferences, ILogger logger) : ApiBaseController
{
    protected const int MaxBodyBytes = 1024 * 1024;

    private static readonly XName FiniteDepth = DavXml.Dav + "propfind-finite-depth";
    private static readonly XName SupportedReport = DavXml.Dav + "supported-report";

    private protected ILogger Logger => logger;

    private protected PreferencesDbContext Preferences => preferences;

    /// <summary>
    /// The resource, fully loaded for its properties, or null once the 404 is written — an unknown
    /// or invalid member name; a shape without a member is never null. <c>request</c> is what the
    /// PROPFIND body asked for, so a context whose gathering COSTS — the principal's addresses, one
    /// call to the platform — is built only where it is read; it is null on PROPPATCH and REPORT,
    /// which carry no property request of this shape.
    /// </summary>
    private protected abstract Task<DavResourceContext?> ContextOrNotFoundAsync(DavResourceKind kind,
        string? collectionName, string? davName, DavPropertyRequest? request,
        CancellationToken cancellationToken);

    private protected abstract (List<XElement> Found, List<XName> Missing) Resolve(
        DavPropertyRequest request, DavResourceContext resource);

    /// <summary>What a <c>Depth: 1</c> lists under a shape, each with the href it is named by.</summary>
    private protected abstract IAsyncEnumerable<(string Href, DavResourceContext Resource)> ChildrenAsync(
        DavResourceContext parent, DavPropertyRequest request, CancellationToken cancellationToken);

    /// <returns>false when the shape does not serve that report — the 403 supported-report</returns>
    private protected abstract Task<bool> ServeReportAsync(DavReportKind report, XDocument body,
        DavResourceContext resource, string requestHref, Trace trace, CancellationToken cancellationToken);

    private protected abstract bool NeedsSnapshot(DavResourceKind kind, DavDepthValue depth);

    /// <summary>RFC 4918 § 9.1 reserves the refusal of <c>Depth: infinity</c> to collections.</summary>
    private protected abstract bool IsCollection(DavResourceKind kind);

    /// <summary>The href a response names this resource by — one table, so PROPFIND and PROPPATCH
    /// cannot spell the same resource two ways.</summary>
    private protected abstract string HrefOf(DavResourceKind kind, Guid userId, string? collectionName,
        string? davName, string? rootHref);

    /// <summary>The trailing-slash form of a collection, or null for a shape that has none.</summary>
    private protected abstract string? CanonicalOf(DavResourceKind kind, Guid userId, string? collectionName);

    /// <summary>
    /// Whether the switch this controller's shapes answer to is on. Read from the claim rather than
    /// from the table: the switch guards devices, and the row was already read to authenticate.
    /// </summary>
    private protected abstract bool SwitchedOn();

    /// <summary>The switch of one protocol. A principal carrying no such claim — a JWT, that is the
    /// webmail's own session — is allowed: the switch guards synchronising devices, not the
    /// browser that flips it.</summary>
    private protected bool Enabled(DavProtocol protocol) =>
        User.FindFirst(protocol is DavProtocol.CardDav
            ? WebmailClaimTypes.CardDav
            : WebmailClaimTypes.CalDav)?.Value != "0";

    private protected Task DispatchAsync(DavResourceKind kind, Guid? userId, string? collectionName,
        string? davName, string? rootHref, CancellationToken cancellationToken) => Request.Method switch
    {
        "PROPFIND" => PropfindAsync(kind, userId, collectionName, davName, rootHref, cancellationToken),
        "PROPPATCH" => ProppatchAsync(kind, userId, collectionName, davName, rootHref, cancellationToken),
        _ => ReportAsync(kind, userId, collectionName, davName, rootHref, cancellationToken),
    };

    private protected Task PropfindAsync(DavResourceKind kind, Guid? userId, string? collectionName,
        string? davName, string? rootHref, CancellationToken cancellationToken) =>
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
                if (IsCollection(kind))
                {
                    await RefuseAsync(trace, FiniteDepth, null, cancellationToken);
                    return;
                }

                depth = DavDepthValue.Zero;
            }

            DavPropertyRequest request;
            try
            {
                var body = await DavXmlReader.ParseAsync(Request.Body, cancellationToken, logger);
                DavPropertyRequest.ValidatePropfindBody(body);
                request = DavPropertyRequest.Parse(body);
            }
            catch (DavBadRequestException ex)
            {
                BodyRefused(ex);
                return;
            }

            var user = AuthenticatedUser;

            async Task WriteAsync(CancellationToken token)
            {
                // The counter BEFORE the members, and this is an order, not a preference: the
                // fallback path without sync-collection holds the ctag it reads as covering the
                // member list it reads next. Read the other way round, a write committing in
                // between is covered by the returned ctag without appearing in the list — the
                // client believes it seen and never asks again. The context carries the counter;
                // the children are streamed after it, inside the same snapshot.
                if (await ContextOrNotFoundAsync(kind, collectionName, davName, request, token)
                    is not { } resource)
                {
                    return;
                }

                var href = HrefOf(kind, user.WebmailUid, collectionName, davName, rootHref);

                await using var writer = await MultiStatusWriter.BeginAsync(Response, token);
                try
                {
                    await WriteResourceAsync(writer, href, request, resource, token);

                    if (depth is DavDepthValue.One)
                    {
                        await foreach (var (childHref, child) in ChildrenAsync(resource, request, token))
                            await WriteResourceAsync(writer, childHref, request, child, token);
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
        }, collectionName);

    /// <summary>
    /// PROPPATCH is the one non-mutating method that is NOT a 405, on every shape the <c>Allow</c>
    /// header announces it on: RFC 4918 § 9.2 requires it of every conforming resource, and Apple's
    /// Contacts.app PROPPATCHes <c>{calendarserver}me-card</c> on the address HOME — sabre documents
    /// that refusing it can make that client crash. The answer is § 9.2.1's for a property one does
    /// not let write, a 207 whose every propstat carries 403, and nothing is stored on the way
    /// through. Virtual for the one shape whose properties ARE written from a client.
    /// </summary>
    private protected virtual Task ProppatchAsync(DavResourceKind kind, Guid? userId, string? collectionName,
        string? davName, string? rootHref, CancellationToken cancellationToken) =>
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

            // Answering 207 on a name that designates nothing would tell the client the member
            // exists — the same lie PROPFIND and GET refuse to tell here.
            if (await ContextOrNotFoundAsync(kind, collectionName, davName, null, cancellationToken)
                is null)
            {
                return;
            }

            await using var writer = await MultiStatusWriter.BeginAsync(Response, cancellationToken);
            await writer.WriteRefusalAsync(
                HrefOf(kind, AuthenticatedUser.WebmailUid, collectionName, davName, rootHref), names,
                cancellationToken);
            trace.Responses = writer.ResponseCount;
        }, collectionName);

    private protected Task ReportAsync(DavResourceKind kind, Guid? userId, string? collectionName,
        string? davName, string? rootHref, CancellationToken cancellationToken) =>
        TracedAsync(userId, kind, async trace =>
        {
            if (davName is not null && !DavName.IsValid(davName))
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
            try
            {
                if (await ContextOrNotFoundAsync(kind, collectionName, davName, null, cancellationToken)
                    is not { } resource)
                {
                    return;
                }

                // After the resolution and not before, like PROPFIND and PROPPATCH: a member name
                // the shape does not hold answers 404 there, and HrefOf would have escaped a null.
                var requestHref = HrefOf(kind, AuthenticatedUser.WebmailUid, collectionName, davName, rootHref);

                var report = document is null ? DavReportKind.Unknown : ReportRequest.KindOf(document);
                if (!await ServeReportAsync(report, document!, resource, requestHref, trace, cancellationToken))
                {
                    // Unknown, or asked off the shape that defines it: a report we do not
                    // serve is a considered 403 — a 500 makes a client loop on it forever.
                    await RefuseAsync(trace, SupportedReport, null, cancellationToken);
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
        }, collectionName);

    /// <summary>
    /// The frame every action runs in. The protocol's switch first, then ownership — a foreign
    /// <c>{userId}</c> answers 404, never 403, which would confirm the principal exists — then the
    /// canonical slash, then the action; and whichever way it leaves, the one log line of
    /// decision 18, with the status the host will write when an exception is on its way out.
    /// </summary>
    private protected async Task TracedAsync(Guid? userId, DavResourceKind kind, Func<Trace, Task> action,
        string? collectionName = null)
    {
        var trace = new Trace();
        int? status = null;
        try
        {
            // Before the ownership check and with no body at all: a switched-off service says
            // nothing about which guid it holds. OPTIONS never reaches here — it is anonymous and
            // answers on every URL, switched off or not.
            if (!SwitchedOn())
            {
                Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (userId is { } target && target != AuthenticatedUser.WebmailUid)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (userId is { } owner && RedirectedToCanonical(kind, owner, collectionName)) return;

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

    /// <summary>A considered 403 carrying its precondition, and the log line's condition with it.</summary>
    private protected Task RefuseAsync(Trace trace, XName condition, XElement? detail, CancellationToken cancellationToken)
    {
        trace.Condition = condition.LocalName;
        return DavError.WriteAsync(Response, StatusCodes.Status403Forbidden, condition, detail,
            cancellationToken, logger);
    }

    /// <summary>The 400 of a body the reader could not judge — never a 500, which a client retries
    /// for ever. A <see cref="BadHttpRequestException"/> never lands here: Kestrel's 413, not our 400.</summary>
    private protected void BodyRefused(DavBadRequestException ex)
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
    private protected async Task<string?> ReadBodyAsync(CancellationToken cancellationToken)
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
    private protected bool RefusedByPreconditions(string? entityTag)
    {
        var ifMatch = HeaderOrNull(Request.Headers.IfMatch);
        if (ifMatch is not null && (entityTag is null || !EntityTagMatcher.Match(ifMatch, entityTag)))
            return true;

        var ifNoneMatch = HeaderOrNull(Request.Headers.IfNoneMatch);
        return ifNoneMatch is not null && entityTag is not null
            && EntityTagMatcher.NoneMatch(ifNoneMatch, entityTag);
    }

    /// <summary>True when If-None-Match spells <c>*</c> — the request only consents to create.</summary>
    private protected bool DemandsCreation() =>
        HeaderOrNull(Request.Headers.IfNoneMatch) is { } header
        && header.Split(',').Any(member => member.Trim() == "*");

    private protected static string? HeaderOrNull(StringValues values) =>
        StringValues.IsNullOrEmpty(values) ? null : values.ToString();

    private protected string? DepthHeader() =>
        Request.Headers.TryGetValue("Depth", out var values) ? values.ToString() : null;

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
    private protected void LogRequest(Trace? trace = null, int? status = null) =>
        DavRequestLog.Write(logger, new DavRequestTrace(
            Request.Method, Request.Path.Value ?? string.Empty, DepthHeader(), trace?.Report,
            trace?.TokenIn, trace?.TokenOut, trace?.Responses ?? 0, status ?? Response.StatusCode,
            trace?.Condition));

    private protected void Capabilities(string allow)
    {
        Response.Headers.Allow = allow;
        DavHeaders.ApplyDav(Response);
        Response.StatusCode = StatusCodes.Status200OK;
        LogRequest();
    }

    private protected void MethodNotAllowed(string allow)
    {
        Response.Headers.Allow = allow;
        Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        LogRequest();
    }

    /// <summary>
    /// A collection URL keeps its trailing slash — without it <see cref="DavPaths.Parse"/>
    /// designates nothing — and the answer is a 308, never the 301 sabre and Radicale use: a 301
    /// lets the client replay as GET, which bare OkHttp does for every verb but PROPFIND, and a
    /// redirected REPORT would lose both its method and its body.
    /// </summary>
    private bool RedirectedToCanonical(DavResourceKind kind, Guid userId, string? collectionName)
    {
        var canonical = CanonicalOf(kind, userId, collectionName);
        if (canonical is null || Request.Path.Value?.EndsWith('/') is not false) return false;

        Response.Headers.Location = canonical;
        Response.StatusCode = StatusCodes.Status308PermanentRedirect;
        return true;
    }

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

    private async Task WriteResourceAsync(MultiStatusWriter writer, string href,
        DavPropertyRequest request, DavResourceContext resource, CancellationToken cancellationToken)
    {
        var (found, missing) = Resolve(request, resource);
        await writer.WriteResourceAsync(href, found, missing, cancellationToken);
    }

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
    /// Opened through the execution strategy the way the sync-collection window is; the
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
        if (!NeedsSnapshot(kind, depth)) return write(cancellationToken);

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
    /// The rank the members are bounded to — the counter this answer's ctag is cut from, so the
    /// two halves say the same thing. No state row bounds nothing: the ctag then renders the
    /// sentinel <c>"0"</c> no live collection emits, so there is no claim for the bound to keep
    /// honest, while bounding at 0 would answer an empty collection — which a client applies by
    /// deleting its copies.
    /// </summary>
    private protected static ulong MemberBound(SyncState? state) => state?.Seq ?? ulong.MaxValue;

    /// <summary>What one request's log line accumulates on its way through an action.</summary>
    private protected sealed class Trace
    {
        public string? Report { get; set; }
        public int Responses { get; set; }
        public string? Condition { get; set; }
        public string? TokenIn { get; set; }
        public string? TokenOut { get; set; }
    }
}
