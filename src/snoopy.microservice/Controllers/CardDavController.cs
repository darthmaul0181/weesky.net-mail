using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Controllers.Dav;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>The CardDAV surface: the address-book tree, from <c>/dav/addressbooks/</c> to the card.</summary>
public sealed class CardDavController(
    IDavContactReader contacts,
    IDavContactWriter writer,
    IContactSyncStore syncStore,
    PreferencesDbContext preferences,
    ILogger<CardDavController> logger) : DavControllerBase(preferences, logger)
{
    /// <summary>Above the card ceiling, so a body over it is read and refused as the announced
    /// <c>403 max-resource-size</c> rather than a transport 413 the announcement never named.</summary>
    private const int PutBodyBytes = 2 * ContactStore.MaxCardBytes;

    private const string CardRoute = "addressbooks/{userId:guid}/" + DavPaths.BookName + "/{*davName}";
    private const string CollectionRoute = "addressbooks/{userId:guid}/" + DavPaths.BookName;

    private static readonly XName ValidAddressData = DavXml.CardDav + "valid-address-data";

    /// <summary>
    /// The three reading verbs, one action per shape. REPORT is bound on the home too, where a 405
    /// under our own <c>Allow</c> would make an RFC 9110 client retry the verb for ever; the default
    /// branch's <c>403 supported-report</c> is the considered answer there.
    /// </summary>
    /// <param name="cancellationToken">the request's own</param>
    [AcceptVerbs("PROPFIND", "PROPPATCH", "REPORT", Route = "addressbooks")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task BookCollectionAsync(CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.AddressBookCollection, AuthenticatedUser.WebmailUid, null, null, null,
            cancellationToken);

    [AcceptVerbs("PROPFIND", "PROPPATCH", "REPORT", Route = "addressbooks/{userId:guid}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task HomeAsync(Guid userId, CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.AddressBookHome, userId, null, null, null, cancellationToken);

    [AcceptVerbs("PROPFIND", "PROPPATCH", "REPORT", Route = CollectionRoute)]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task CollectionAsync(Guid userId, CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.AddressBook, userId, null, null, null, cancellationToken);

    /// <summary>RFC 6352 § 8.6 and § 8.7 define query and multiget on address resources too, and
    /// supported-report-set says so on every card — without REPORT here the header lies.</summary>
    [AcceptVerbs("PROPFIND", "PROPPATCH", "REPORT", Route = CardRoute)]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task CardAsync(Guid userId, string? davName, CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.Card, userId, null, davName, null, cancellationToken);

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

            var entityTag = CardDavProperties.EntityTag(card);
            Response.Headers.ETag = entityTag;
            Response.Headers.LastModified = DavPropertyTables.HttpDate(card.UpdatedAt);
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
            var body = await ReadBodyAsync(cancellationToken);

            var card = await contacts.FindAsync(user.WebmailUid, davName, cancellationToken);
            var entityTag = card is null ? null : CardDavProperties.EntityTag(card);

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

            if (RefusedByPreconditions(CardDavProperties.EntityTag(card)))
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
        TracedAsync(userId, DavResourceKind.AddressBook, async trace =>
        {
            trace.Condition = await AnswerOutcomeAsync(
                await writer.DeleteAllAsync(AuthenticatedUser.WebmailUid, cancellationToken),
                cancellationToken);
        });

    /// <summary>
    /// Capabilities, answered off the URL shape alone: <c>[AllowAnonymous]</c> on the OPTIONS
    /// actions and on no other action, because a client asks what the server can do before it holds
    /// any credentials — which is also why it consults no store and reveals nothing a URL did not
    /// already carry.
    /// </summary>
    [AcceptVerbs("OPTIONS", Route = "addressbooks")]
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
    [Route("addressbooks")]
    [Route("addressbooks/{userId:guid}")]
    public void MethodNotAllowedOnHome() => MethodNotAllowed(DavHeaders.HomeAllow);

    [Route(CollectionRoute)]
    public void MethodNotAllowedOnCollection() => MethodNotAllowed(DavHeaders.CollectionAllow);

    [Route(CardRoute)]
    public void MethodNotAllowedOnCard() => MethodNotAllowed(DavHeaders.CardAllow);

    /// <summary>
    /// The card on a card, the sync state on the book — only the collection's properties read it,
    /// and it is the counter every child listed under it is bounded to.
    /// </summary>
    private protected override async Task<DavResourceContext?> ContextOrNotFoundAsync(DavResourceKind kind,
        string? collectionName, string? davName, DavPropertyRequest? request,
        CancellationToken cancellationToken)
    {
        DavCard? card = null;
        if (kind is DavResourceKind.Card && (card = await FindCardOr404Async(davName, cancellationToken)) is null)
            return null;

        var user = AuthenticatedUser;
        var state = kind is DavResourceKind.AddressBook
            ? await syncStore.ReadStateAsync(user.WebmailUid, cancellationToken)
            : null;
        return new DavResourceContext(kind, user.WebmailUid, user.Email, card, state);
    }

    private protected override (List<XElement> Found, List<XName> Missing) Resolve(
        DavPropertyRequest request, DavResourceContext resource) =>
        CardDavProperties.Resolve(request, resource);

    /// <summary>
    /// The one member a Depth: 1 lists on the two shapes that hold exactly one, and it is always
    /// THIS account's: no <c>{userId}</c> reaches the intermediate shape, so serving another guid's
    /// child would be listing a resource the caller is not. The book's own members are streamed
    /// instead, being many, and bounded to the counter the parent's ctag was cut from.
    /// </summary>
    private protected override async IAsyncEnumerable<(string Href, DavResourceContext Resource)> ChildrenAsync(
        DavResourceContext parent, DavPropertyRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userId = parent.UserId;
        switch (parent.Kind)
        {
            case DavResourceKind.AddressBookCollection:
                yield return (DavPaths.Home(userId), parent with { Kind = DavResourceKind.AddressBookHome });
                break;
            case DavResourceKind.AddressBookHome:
                yield return (DavPaths.Collection(userId), parent with
                {
                    Kind = DavResourceKind.AddressBook,
                    State = await syncStore.ReadStateAsync(userId, cancellationToken),
                });
                break;
            case DavResourceKind.AddressBook:
                await foreach (var member in contacts.StreamAsync(userId, MemberBound(parent.State), cancellationToken))
                {
                    yield return (DavPaths.Card(userId, member.DavName),
                        parent with { Kind = DavResourceKind.Card, Card = member });
                }

                break;
        }
    }

    /// <summary>
    /// Each report is gated by the shapes whose supported-report-set announces it — RFC 6352 § 8.6
    /// and § 8.7 for the book and an address resource, RFC 3253 § 3.8 for everything but a card (no
    /// property of a card is href-valued), RFC 6578 § 3.1 for the collection alone. Served off those
    /// shapes, a report would answer where the resource never claimed it — the mirror of the
    /// announcement that made DAVx5 loop.
    /// </summary>
    private protected override async Task<bool> ServeReportAsync(DavReportKind report, XDocument body,
        DavResourceContext resource, string requestHref, Trace trace, CancellationToken cancellationToken)
    {
        var kind = resource.Kind;
        switch (report)
        {
            case DavReportKind.Multiget when kind is DavResourceKind.AddressBook or DavResourceKind.Card:
                trace.Responses = await MultigetReport.WriteAsync(Response, body, requestHref,
                    MemberSource(resource), cancellationToken);
                return true;
            case DavReportKind.ExpandProperty when kind is DavResourceKind.AddressBookHome or DavResourceKind.AddressBook:
                trace.Responses = await ExpandPropertyReport.WriteAsync(Response, body, resource, requestHref,
                    nested => DavPrincipalController.NestedContext(nested, resource.UserId, resource.PrincipalAddress),
                    Resolve, cancellationToken);
                return true;
            case DavReportKind.Query when kind is DavResourceKind.AddressBook or DavResourceKind.Card:
                // A query on a card is scoped to that card alone — the context carries it.
                trace.Responses = await AddressBookQueryReport.WriteAsync(Response, body, requestHref,
                    resource.UserId, resource.PrincipalAddress, resource.Card, contacts, cancellationToken);
                return true;
            case DavReportKind.SyncCollection when kind is DavResourceKind.AddressBook:
                // tokenIn BEFORE the call: the refusal path is the one the field exists
                // for — "a token refused in a loop" is separable from the four other
                // empty-book causes only by reading WHICH token looped.
                trace.TokenIn = DavSyncToken.ForLog(
                    body.Root!.Element(DavXml.Dav + "sync-token")?.Value);
                var source = MemberSource(resource);
                var window = await ReadSyncWindowAsync(body.Root!, resource.UserId, source, cancellationToken);
                var sync = await SyncCollectionReport.WriteAsync(Response, body, requestHref, DepthHeader(),
                    window, source, cancellationToken);
                trace.Responses = sync.Responses;
                trace.TokenOut = DavSyncToken.ForLog(sync.TokenOut);
                return true;
            default:
                return false;
        }
    }

    private protected override bool SwitchedOn() => Enabled(DavProtocol.CardDav);

    private protected override bool NeedsSnapshot(DavResourceKind kind, DavDepthValue depth) =>
        kind is DavResourceKind.AddressBook && depth is DavDepthValue.One;

    private protected override bool IsCollection(DavResourceKind kind) =>
        kind is DavResourceKind.AddressBookCollection or DavResourceKind.AddressBookHome or DavResourceKind.AddressBook;

    private protected override string HrefOf(DavResourceKind kind, Guid userId, string? collectionName,
        string? davName, string? rootHref) => kind switch
    {
        DavResourceKind.AddressBookCollection => DavPaths.BookCollection,
        DavResourceKind.AddressBookHome => DavPaths.Home(userId),
        DavResourceKind.AddressBook => DavPaths.Collection(userId),
        _ => DavPaths.Card(userId, davName!),
    };

    private protected override string? CanonicalOf(DavResourceKind kind, Guid userId, string? collectionName) =>
        kind switch
        {
            DavResourceKind.AddressBookCollection => DavPaths.BookCollection,
            DavResourceKind.AddressBookHome => DavPaths.Home(userId),
            DavResourceKind.AddressBook => DavPaths.Collection(userId),
            _ => null,
        };

    private CardMemberSource MemberSource(DavResourceContext resource) =>
        new(contacts, resource.UserId, resource.PrincipalAddress);

    /// <summary>
    /// The counter and the tombstones of a sync-collection, read in one snapshot that STOPS there.
    /// Those two together is what <see cref="IContactSyncStore.ReadStateAsync"/>'s contract demands:
    /// on MySQL's REPEATABLE READ the first SELECT pins one InnoDB read view, so a prune committing
    /// between them cannot delete deletions this response still owes under a watermark it read as
    /// lower — the client would then keep those cards for ever, with no error and nothing to signal
    /// it, since the token it files next is above the new watermark and is accepted. Opened through
    /// the execution strategy the way ContactStore.InTransactionAsync is. Nothing is written to the
    /// response inside it, so a retrying strategy, were one ever configured, replays reads alone and
    /// can never write the document twice.
    /// </summary>
    private Task<SyncCollectionReport.SyncWindow> ReadSyncWindowAsync(XElement root, Guid userId,
        CardMemberSource source, CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task<SyncCollectionReport.SyncWindow>> read = async token =>
        {
            await using var transaction = await Preferences.Database.BeginTransactionAsync(token);
            // The counter first — an empty book needs an epoch to form its token, hence the create.
            var state = await syncStore.ReadOrCreateStateAsync(userId, token);
            var window = await SyncCollectionReport.ReadWindowAsync(root, state, source, token);
            await transaction.CommitAsync(token);
            return window;
        };

        return Preferences.Database.CreateExecutionStrategy().ExecuteAsync(read, cancellationToken);
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

    /// <summary>
    /// Archives a refused body before its 412 leaves — only when it decodes: the storage is text,
    /// and what it cannot give back verbatim it must not pretend to keep.
    /// </summary>
    private async Task ArchiveRefusedBodyAsync(
        Guid userId, string davName, string? body, CancellationToken cancellationToken)
    {
        if (body is null) return;
        if (!await writer.ArchiveRejectedAsync(userId, davName, body, cancellationToken))
            Logger.LogInformation("The refused PUT body for {DavName} was not archived", davName);
    }

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
    /// <see cref="CardDavOutcomeTranslator"/> is exactly how a branch ends up missing and a client ends
    /// up retrying a 500 on the same card for ever.
    /// </summary>
    private async Task<string?> AnswerOutcomeAsync(
        DavWriteOutcome outcome, CancellationToken cancellationToken)
    {
        await CardDavOutcomeTranslator.WriteAsync(Response, outcome, cancellationToken, Logger);
        return CardDavOutcomeTranslator.ConditionOf(outcome.Status)?.LocalName;
    }
}
