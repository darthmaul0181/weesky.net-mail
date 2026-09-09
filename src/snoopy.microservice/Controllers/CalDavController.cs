using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Controllers.Dav;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.CalDav;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>The CalDAV surface: the calendar tree, from <c>/dav/calendars/</c> to the event.</summary>
public sealed class CalDavController(
    IDavCalendarReader calendars,
    IDavCalendarWriter writer,
    ICalendarStore collections,
    ICalendarSyncStore syncStore,
    PreferencesDbContext preferences,
    TimeProvider clock,
    ILogger<CalDavController> logger) : DavControllerBase(preferences, logger)
{
    /// <summary>Above the resource ceiling, so a body over it is read and refused as the announced
    /// <c>403 max-resource-size</c> rather than a transport 413 the announcement never named.</summary>
    private const int PutBodyBytes = 2 * IcsGuards.MaxIcsBytes;

    /// <summary>The roots RFC 4791 § 5.3.1 and RFC 5689 § 3 give the two creation verbs; neither
    /// is a <c>DAV:multistatus</c>, and neither carries an href.</summary>
    private static readonly XName MkcalendarResponse = DavXml.CalDav + "mkcalendar-response";

    private static readonly XName MkcolResponse = DavXml.Dav + "mkcol-response";

    private const string HomeRoute = "calendars/{userId:guid}";
    private const string CalendarRoute = HomeRoute + "/{calendarName}";
    private const string EventRoute = CalendarRoute + "/{*davName}";

    /// <summary>
    /// The three reading verbs, one action per shape. REPORT is bound on every one of them,
    /// including those that serve no report: a 405 under our own <c>Allow</c> would make an
    /// RFC 9110 client retry the verb for ever, where the base's <c>403 supported-report</c> is a
    /// considered answer.
    /// </summary>
    /// <param name="cancellationToken">the request's own</param>
    [AcceptVerbs("PROPFIND", "PROPPATCH", "REPORT", Route = "calendars")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task CalendarCollectionAsync(CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.CalendarCollection, AuthenticatedUser.WebmailUid, null, null,
            null, cancellationToken);

    [AcceptVerbs("PROPFIND", "PROPPATCH", "REPORT", Route = HomeRoute)]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task HomeAsync(Guid userId, CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.CalendarHome, userId, null, null, null, cancellationToken);

    [AcceptVerbs("PROPFIND", "PROPPATCH", "REPORT", Route = CalendarRoute)]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task CalendarAsync(Guid userId, string calendarName, CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.Calendar, userId, calendarName, null, null, cancellationToken);

    [AcceptVerbs("PROPFIND", "PROPPATCH", "REPORT", Route = EventRoute)]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task EventAsync(Guid userId, string calendarName, string? davName,
        CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.Event, userId, calendarName, davName, null, cancellationToken);

    /// <summary>
    /// The file, verbatim. HEAD is bound to the same action, so it answers the same headers by
    /// construction — Content-Length included, which is what makes it worth issuing.
    /// </summary>
    [HttpGet(EventRoute)]
    [HttpHead(EventRoute)]
    public Task GetEventAsync(Guid userId, string calendarName, string? davName,
        CancellationToken cancellationToken) =>
        TracedAsync(userId, DavResourceKind.Event, async trace =>
        {
            if (await FindCalendarOr404Async(calendarName, cancellationToken) is not { } calendar)
                return;
            if (await FindEventOr404Async(calendar, davName, cancellationToken) is not { } member)
                return;

            var entityTag = CalDavProperties.EntityTag(member);
            Response.Headers.ETag = entityTag;
            Response.Headers.LastModified = DavPropertyTables.HttpDate(member.UpdatedAt);
            DavHeaders.ApplyDav(Response);

            if (EntityTagMatcher.NoneMatch(Request.Headers.IfNoneMatch, entityTag))
            {
                Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            // Never through a formatter: a re-encode — a BOM, a line ending, a charset — would
            // leave the ETag describing something other than what goes out.
            var bytes = Encoding.UTF8.GetBytes(member.IcsRaw);
            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = DavHeaders.CalendarContentType;
            Response.ContentLength = bytes.Length;
            trace.Responses = 1;

            // Explicit rather than left to the host: Kestrel drops a HEAD body, TestServer does not.
            if (HttpMethods.IsHead(Request.Method)) return;

            await Response.Body.WriteAsync(bytes, cancellationToken);
        }, calendarName);

    /// <summary>
    /// PUT — create or replace one resource, stored verbatim. The preconditions are evaluated
    /// FIRST (RFC 7232 puts them before any processing of the body), and a PUT they refuse
    /// archives its body under the <c>Rejected</c> cause before the 412 leaves: the calendar never
    /// held that version, but the server does — the bytes are read and bounded — and throwing them
    /// away is a decision, not a fatality, when DAVx5 applies "the server wins" without consulting
    /// anyone. Only a body that decodes as strict UTF-8 is archived: the storage is text, and what
    /// it cannot give back verbatim it must not pretend to keep.
    /// </summary>
    [HttpPut(EventRoute)]
    [RequestSizeLimit(PutBodyBytes)]
    public Task PutEventAsync(Guid userId, string calendarName, string? davName,
        CancellationToken cancellationToken) =>
        TracedAsync(userId, DavResourceKind.Event, async trace =>
        {
            if (string.IsNullOrEmpty(davName))
            {
                // UNREACHABLE, and kept: the calendar template outranks this catch-all one. It
                // stays because it is what proves davName non-null below; a bare null-forgiving
                // operator instead would be an NRE — a 500 — the day route precedence changes.
                await RefuseAsync(trace, CalDavError.CalendarCollectionLocationOk, null, cancellationToken);
                return;
            }

            if (!DavName.IsValid(davName))
            {
                // Decision 5 of 4c: a name this collection will not hold is refused by a considered
                // answer, never by a routing 404, which a client reads as "this collection does
                // not contain that".
                await RefuseAsync(trace, CalDavError.ValidCalendarData, null, cancellationToken);
                return;
            }

            if (await FindCalendarOr404Async(calendarName, cancellationToken) is not { } calendar)
                return;

            var user = AuthenticatedUser;
            var body = await ReadBodyAsync(cancellationToken);

            var member = await calendars.FindAsync(calendar.Id, davName, cancellationToken);
            var entityTag = member is null ? null : CalDavProperties.EntityTag(member);

            if (RefusedByPreconditions(entityTag))
            {
                await ArchiveRefusedBodyAsync(user.WebmailUid, calendar.Id, davName, body, cancellationToken);
                Response.StatusCode = StatusCodes.Status412PreconditionFailed;
                return;
            }

            if (body is null)
            {
                await RefuseAsync(trace, CalDavError.ValidCalendarData, null, cancellationToken);
                return;
            }

            // RFC 4791 § 5.3.2.1: supported-calendar-data is about the MEDIA TYPE, and this is the
            // one layer that sees it. An absent header is not a refusal — clients omit it — but a
            // header naming something else is, and it is not the same answer as invalid data.
            if (Request.ContentType is { Length: > 0 } contentType
                && !DavHeaders.MediaTypeOf(contentType).Equals(CalDavProperties.CalendarDataMediaType, StringComparison.OrdinalIgnoreCase))
            {
                await RefuseAsync(trace, CalDavError.SupportedCalendarData, null, cancellationToken);
                return;
            }

            // The header rides along: the pre-check above ran before any lock, so the decisive
            // If-Match comparison is the gate's, under the state lock.
            var outcome = await writer.PutAsync(user.WebmailUid, calendar.Id, davName, body,
                cancellationToken, createOnly: DemandsCreation(),
                ifMatch: HeaderOrNull(Request.Headers.IfMatch));
            // A race's loser, refused INSIDE the gate: nothing was written, so the body genuinely
            // never reached the calendar and earns the same archive as any 412.
            if (outcome.Status is DavWriteStatus.AlreadyExists or DavWriteStatus.PreconditionFailed)
                await ArchiveRefusedBodyAsync(user.WebmailUid, calendar.Id, davName, body, cancellationToken);

            trace.Condition = await AnswerPutOutcomeAsync(outcome, DemandsCreation(), cancellationToken);
        }, calendarName);

    /// <summary>
    /// A PUT whose target sits directly under the home is not inside any calendar: RFC 4791
    /// § 5.3.2.1's <c>calendar-collection-location-ok</c>, not the 405 of a verb the shape does
    /// not serve. No redirect first — the trailing slash is a collection's, and this is no
    /// collection being addressed.
    /// </summary>
    [HttpPut(CalendarRoute)]
    public Task PutOffACalendarAsync(Guid userId, CancellationToken cancellationToken) =>
        TracedAsync(userId, DavResourceKind.Calendar, trace =>
            RefuseAsync(trace, CalDavError.CalendarCollectionLocationOk, null, cancellationToken));

    /// <summary>
    /// DELETE — remove one resource. Ownership, then the read, then the preconditions, then the
    /// removal, and the removal alone lays a tombstone: one laid beside a 412 would erase
    /// everywhere a resource the server has just said it was keeping. No
    /// <c>[RequestSizeLimit]</c>: a DELETE carries no body and this action reads none.
    /// </summary>
    [HttpDelete(EventRoute)]
    public Task DeleteEventAsync(Guid userId, string calendarName, string? davName,
        CancellationToken cancellationToken) =>
        TracedAsync(userId, DavResourceKind.Event, async trace =>
        {
            // A name the calendar will not hold designates nothing, so it is the same 404 an
            // unknown name gets — never PUT's 403, which answers about a resource this request is
            // not bringing.
            if (await FindCalendarOr404Async(calendarName, cancellationToken) is not { } calendar)
                return;
            if (await FindEventOr404Async(calendar, davName, cancellationToken) is not { } member)
                return;

            if (RefusedByPreconditions(CalDavProperties.EntityTag(member)))
            {
                Response.StatusCode = StatusCodes.Status412PreconditionFailed;
                return;
            }

            // The header rides along, as on PUT: the check above is the fast path, the gate's
            // re-comparison under the state lock is the decision.
            trace.Condition = await AnswerOutcomeAsync(
                await writer.DeleteAsync(AuthenticatedUser.WebmailUid, calendar.Id, member.DavName,
                    cancellationToken, HeaderOrNull(Request.Headers.IfMatch)),
                cancellationToken);
        }, calendarName);

    /// <summary>
    /// MKCALENDAR and the extended MKCOL — two doors, one creation (§ 11). The URL segment the
    /// client names becomes the collection's <c>dav_name</c> (décision 2 of the overview), so this
    /// is the one shape that serves them: a client creates by naming the calendar to be born, not
    /// its parent, and on the parent itself both are the 405 RFC 4918 § 9.3.1 reserves to a
    /// resource that already exists.
    /// </summary>
    [AcceptVerbs("MKCALENDAR", "MKCOL", Route = CalendarRoute)]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task MakeCalendarAsync(Guid userId, string calendarName, CancellationToken cancellationToken) =>
        TracedAsync(userId, DavResourceKind.Calendar, async trace =>
        {
            // RFC 4791 § 5.3.1 Marshalling and RFC 4918 § 9.3, without condition: the header goes
            // on the response, so it is posed once here rather than on each of the nine exits.
            Response.Headers.CacheControl = DavHeaders.NoCache;

            if (!DavName.IsValid(calendarName))
            {
                // Decision 5 of 4c, bare: no precondition names a segment this tree will not hold,
                // and a routing 404 would say the collection is missing rather than impossible.
                Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var extended = Request.Method == "MKCOL";
            MkCalendarRequest request;
            XDocument? body;
            try
            {
                body = await DavXmlReader.ParseAsync(Request.Body, cancellationToken, Logger);
                request = MkCalendarRequest.Parse(body, extended);
            }
            catch (DavBadRequestException ex)
            {
                BodyRefused(ex);
                return;
            }

            // RFC 5689 § 3 scopes every word of itself to the EXTENDED MKCOL, the one carrying a
            // request body; a bodyless MKCOL stays the standard one of RFC 4918 § 9.3.
            var extendedWithBody = extended && body is not null;

            if (request.ResourceTypeRefused)
            {
                await RefuseCreationAsync(trace, extendedWithBody, CalendarPropertyValue.ResourceType,
                    CalDavError.ValidResourceType, cancellationToken);
                return;
            }

            if (request.TimeZoneRefused)
            {
                await RefuseCreationAsync(trace, extendedWithBody, CalendarPropertyValue.TimeZone,
                    CalDavError.ValidCalendarData, cancellationToken);
                return;
            }

            if (request.AsksUnsupportedComponent)
            {
                // RFC 4791 § 5.3.1 and RFC 5689 § 3: each verb answers under its OWN root, never
                // DAV:multistatus, and neither carries an href — nothing was created to name.
                await MultiStatusWriter.WriteCreationRefusalAsync(Response,
                    extendedWithBody ? MkcolResponse : MkcalendarResponse, [],
                    [CalendarPropertyValue.ComponentSet], CalDavError.SupportedCalendarComponent,
                    cancellationToken);
                trace.Responses = 1;
                trace.Condition = CalendarPropertyValue.ComponentSet.LocalName;
                return;
            }

            if (request.Refused.Count > 0)
            {
                // § 9.2's atomicity: the named property fails, and every other WRITABLE property the
                // body itself asked for fails in dependency — never a property the client never sent.
                await MultiStatusWriter.WriteCreationRefusalAsync(Response,
                    extendedWithBody ? MkcolResponse : MkcalendarResponse,
                    request.NamedWritable.Except(request.Refused).ToList(),
                    request.Refused, CalDavError.CannotModifyProtectedProperty, cancellationToken);
                trace.Responses = 1;
                trace.Condition = CalDavError.CannotModifyProtectedProperty.LocalName;
                return;
            }

            // The segment as the display name when the client sends none: a calendar with no name
            // is one every client lists as a blank row.
            var write = new CalendarWrite(request.DisplayName ?? calendarName, request.Description,
                request.Color, request.Order, request.TimeZoneId);
            var created = await collections.CreateNamedAsync(
                AuthenticatedUser.WebmailUid, calendarName, write, cancellationToken);
            if (created.IsSuccess)
            {
                Response.StatusCode = StatusCodes.Status201Created;
                DavHeaders.ApplyDav(Response);
                return;
            }

            if (created.Error == CalendarStore.CapReached)
            {
                Response.StatusCode = StatusCodes.Status507InsufficientStorage;
                return;
            }

            if (created.Error == CalendarStore.NameTaken)
            {
                // The same answer the home gets, and for the same reason: the collection is there.
                // RFC 4918 § 9.3.1 gives the 405 and RFC 7231 § 6.5.5 the Allow; § 16 asks that a
                // refusal name its precondition, so the client reads a reason and not a code.
                Response.Headers.Allow = DavHeaders.CalendarAllow;
                await DavError.WriteAsync(Response, StatusCodes.Status405MethodNotAllowed,
                    CalDavError.ResourceMustBeNull, null, cancellationToken, Logger);
                trace.Condition = CalDavError.ResourceMustBeNull.LocalName;
                return;
            }

            await RefuseAsync(trace, CalDavError.ValidCalendarData, null, cancellationToken);
        }, calendarName);

    /// <summary>RFC 5689 § 3 wants a mkcol-response holding propstat for a property failure; RFC
    /// 4791 § 5.3.1 leaves MKCALENDAR on RFC 4918 § 16's bare error. One property, two shapes.</summary>
    private async Task RefuseCreationAsync(Trace trace, bool extendedWithBody, XName property,
        XName condition, CancellationToken cancellationToken)
    {
        if (!extendedWithBody)
        {
            await RefuseAsync(trace, condition, null, cancellationToken);
            return;
        }

        await MultiStatusWriter.WriteCreationRefusalAsync(Response, MkcolResponse, [], [property],
            condition, cancellationToken);
        trace.Responses = 1;
        trace.Condition = condition.LocalName;
    }

    /// <summary>
    /// A creation aimed INSIDE a calendar: RFC 4791 § 5.3.1's
    /// <c>calendar-collection-location-ok</c> — the place is wrong, which is what that condition
    /// says and what a 405 would not. The route exists to answer this, never to route it away.
    /// </summary>
    [AcceptVerbs("MKCALENDAR", "MKCOL", Route = EventRoute)]
    public Task MakeCalendarUnderACalendarAsync(Guid userId, CancellationToken cancellationToken) =>
        TracedAsync(userId, DavResourceKind.Event, trace =>
        {
            // Same header, same reason as MakeCalendarAsync: this is a creation refusal too.
            Response.Headers.CacheControl = DavHeaders.NoCache;
            return RefuseAsync(trace, CalDavError.CalendarCollectionLocationOk, null, cancellationToken);
        });

    /// <summary>
    /// DELETE on a collection: the secondary one goes, and <c>default</c> is EMPTIED instead —
    /// a user with no calendar has nowhere to write, so the collection survives with a tombstone
    /// per resource, which is what tells every other device each one is gone. <c>If-Match</c> is
    /// not consulted: a collection carries no entity tag to compare it against.
    /// </summary>
    [HttpDelete(CalendarRoute)]
    public Task DeleteCalendarAsync(Guid userId, string calendarName, CancellationToken cancellationToken) =>
        TracedAsync(userId, DavResourceKind.Calendar, async trace =>
        {
            // Resolved here, and this is what makes DeleteAllAsync's answer about a collection
            // this user actually holds: an unknown or foreign segment is the 404 it would be for
            // any other verb.
            if (await FindCalendarOr404Async(calendarName, cancellationToken) is not { } calendar)
                return;

            if (calendar.DavName == CalendarStore.DefaultDavName)
            {
                trace.Condition = await AnswerOutcomeAsync(
                    await writer.DeleteAllAsync(AuthenticatedUser.WebmailUid, calendar.Id,
                        cancellationToken),
                    cancellationToken);
                return;
            }

            var removed = await collections.DeleteAsync(
                AuthenticatedUser.WebmailUid, calendar.Id, cancellationToken);
            Response.StatusCode = removed.IsSuccess
                ? StatusCodes.Status204NoContent
                : StatusCodes.Status404NotFound;
        }, calendarName);

    /// <summary>
    /// Capabilities, answered off the URL shape alone: <c>[AllowAnonymous]</c> on the OPTIONS
    /// actions and on no other, because a client asks what the server can do before it holds any
    /// credentials — which is also why they consult no store and reveal nothing a URL did not
    /// already carry, switched off or not.
    /// </summary>
    [AcceptVerbs("OPTIONS", Route = "calendars")]
    [AllowAnonymous]
    public void OptionsCalendarCollection() => Capabilities(DavHeaders.HomeAllow);

    [AcceptVerbs("OPTIONS", Route = HomeRoute)]
    [AllowAnonymous]
    public void OptionsHome() => Capabilities(DavHeaders.CalendarHomeAllow);

    [AcceptVerbs("OPTIONS", Route = CalendarRoute)]
    [AllowAnonymous]
    public void OptionsCalendar() => Capabilities(DavHeaders.CalendarAllow);

    [AcceptVerbs("OPTIONS", Route = EventRoute)]
    [AllowAnonymous]
    public void OptionsEvent() => Capabilities(DavHeaders.EventAllow);

    /// <summary>
    /// Last on purpose, and bound to no verb: carrying no method metadata, these four actions score
    /// below every real route above, so action selection reaches them only when nothing else
    /// answers the verb. They carry <c>Allow</c> and nothing else — routing supplies one of its
    /// own, but it is the union of the verbs bound on the template, which on the calendar omits the
    /// two creation verbs that shape announces and on the event names none of PUT and DELETE.
    /// </summary>
    [Route("calendars")]
    public void MethodNotAllowedOnCalendarCollection() => MethodNotAllowed(DavHeaders.HomeAllow);

    [Route(HomeRoute)]
    public void MethodNotAllowedOnHome() => MethodNotAllowed(DavHeaders.CalendarHomeAllow);

    [Route(CalendarRoute)]
    public void MethodNotAllowedOnCalendar() => MethodNotAllowed(DavHeaders.CalendarAllow);

    [Route(EventRoute)]
    public void MethodNotAllowedOnEvent() => MethodNotAllowed(DavHeaders.EventAllow);

    /// <summary>
    /// The calendar on both member shapes, the event on its own, and the sync state on the calendar
    /// alone — only its properties read it, and it is the counter every child listed under it is
    /// bounded to.
    /// </summary>
    private protected override async Task<DavResourceContext?> ContextOrNotFoundAsync(
        DavResourceKind kind, string? collectionName, string? davName, DavPropertyRequest? request,
        CancellationToken cancellationToken)
    {
        DavCalendar? calendar = null;
        DavEvent? member = null;
        SyncState? state = null;

        if (kind is DavResourceKind.Calendar or DavResourceKind.Event)
        {
            if (await FindCalendarOr404Async(collectionName, cancellationToken) is not { } found)
                return null;

            calendar = found;
            if (kind is DavResourceKind.Event)
            {
                if (await FindEventOr404Async(found, davName, cancellationToken) is not { } row)
                    return null;

                member = row;
            }
            else
            {
                state = await syncStore.ReadStateAsync(found.Id, cancellationToken);
            }
        }

        var user = AuthenticatedUser;
        return new DavResourceContext(kind, user.WebmailUid, user.Email, null, state,
            CollectionName: collectionName, Calendar: calendar, Event: member);
    }

    private protected override (List<XElement> Found, List<XName> Missing) Resolve(
        DavPropertyRequest request, DavResourceContext resource) =>
        CalDavProperties.Resolve(request, resource, clock);

    /// <summary>
    /// The calendar is the one shape of this tree whose properties a client really writes (§ 11):
    /// a 207 of mixed statuses, the accepted ones stored through
    /// <see cref="ICalendarStore.UpdateAsync"/>. Everywhere else — the home, the collection of
    /// homes, an event — the base's blanket 403 stands, as 4c decision 16 has it.
    /// </summary>
    private protected override Task ProppatchAsync(DavResourceKind kind, Guid? userId,
        string? collectionName, string? davName, string? rootHref, CancellationToken cancellationToken) =>
        kind is DavResourceKind.Calendar
            ? PatchCalendarAsync(userId, collectionName, cancellationToken)
            : base.ProppatchAsync(kind, userId, collectionName, davName, rootHref, cancellationToken);

    private Task PatchCalendarAsync(Guid? userId, string? collectionName,
        CancellationToken cancellationToken) =>
        TracedAsync(userId, DavResourceKind.Calendar, async trace =>
        {
            CalendarPropertyUpdate update;
            try
            {
                update = CalendarPropertyUpdate.Parse(
                    await DavXmlReader.ParseAsync(Request.Body, cancellationToken, Logger));
            }
            catch (DavBadRequestException ex)
            {
                BodyRefused(ex);
                return;
            }

            // Answering 207 on a segment that designates nothing would tell the client the
            // collection exists — the same lie PROPFIND and GET refuse to tell here.
            if (await FindCalendarOr404Async(collectionName, cancellationToken) is not { } calendar)
                return;

            // Built from the calendar as it stands: UpdateAsync overwrites the display name
            // unconditionally, so a body that never named it would blank it.
            var stored = update.Accepted.Count == 0 || (await collections.UpdateAsync(
                AuthenticatedUser.WebmailUid, calendar.Id, Written(calendar, update),
                cancellationToken)).IsSuccess;

            // Listed in the table's own order and not the dictionary's, which no host promises.
            IReadOnlyList<XName> ok = stored
                ? [.. CalendarPropertyValue.Writable.Where(update.Accepted.ContainsKey)]
                : [];
            IReadOnlyList<XName> refused = stored
                ? update.Refused
                : [.. CalendarPropertyValue.Writable.Where(update.Accepted.ContainsKey), .. update.Refused];

            // No rank and no ctag: none of these five is a resource, and advancing the counter
            // would make every phone resync a collection nothing in it changed.
            await using var writer207 = await MultiStatusWriter.BeginAsync(Response, cancellationToken);
            await writer207.WriteMixedAsync(
                DavPaths.Calendar(AuthenticatedUser.WebmailUid, calendar.DavName), ok, refused,
                cancellationToken);
            trace.Responses = writer207.ResponseCount;
        }, collectionName);

    /// <summary>The write a PROPPATCH composes: what the body accepted, and the calendar's own
    /// value for every property it left alone.</summary>
    private static CalendarWrite Written(DavCalendar calendar, CalendarPropertyUpdate update)
    {
        var accepted = update.Accepted;
        return new CalendarWrite(
            accepted.TryGetValue(CalendarPropertyValue.DisplayName, out var name)
                ? name!
                : calendar.DisplayName,
            // A DAV:remove carries no value, and an empty description is what it leaves.
            accepted.TryGetValue(CalendarPropertyValue.Description, out var description)
                ? description ?? string.Empty
                : null,
            accepted.GetValueOrDefault(CalendarPropertyValue.Color),
            accepted.GetValueOrDefault(CalendarPropertyValue.Order) is { } order
                ? CalendarPropertyValue.Rank(order)
                : null,
            update.TimeZoneId);
    }

    /// <summary>
    /// The one member the collection of homes holds, then every calendar of the account — hidden
    /// ones included, the checkbox being a display state — and then a calendar's own resources,
    /// streamed and bounded to the counter its ctag was cut from.
    /// </summary>
    private protected override async IAsyncEnumerable<(string Href, DavResourceContext Resource)> ChildrenAsync(
        DavResourceContext parent, DavPropertyRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userId = parent.UserId;
        switch (parent.Kind)
        {
            case DavResourceKind.CalendarCollection:
                yield return (DavPaths.CalendarHome(userId),
                    parent with { Kind = DavResourceKind.CalendarHome });
                break;
            case DavResourceKind.CalendarHome:
                var rows = await calendars.ListAsync(userId, cancellationToken);
                // Every state read before the first response is written, inside the one snapshot
                // NeedsSnapshot opens: a counter read after the collection it answers for would
                // cover a write the answer does not carry.
                Dictionary<Guid, SyncState?> states = [];
                foreach (var row in rows)
                    states[row.Id] = await syncStore.ReadStateAsync(row.Id, cancellationToken);

                foreach (var row in rows)
                {
                    yield return (DavPaths.Calendar(userId, row.DavName), parent with
                    {
                        Kind = DavResourceKind.Calendar,
                        CollectionName = row.DavName,
                        Calendar = row,
                        State = states[row.Id],
                    });
                }

                break;
            case DavResourceKind.Calendar:
                var members = calendars.StreamAsync(
                    parent.Calendar!.Id, MemberBound(parent.State), cancellationToken);
                await foreach (var member in members)
                {
                    yield return (DavPaths.Event(userId, parent.Calendar.DavName, member.DavName),
                        parent with { Kind = DavResourceKind.Event, Event = member });
                }

                break;
        }
    }

    /// <summary>
    /// The reports of § 7, § 8 and § 9, each on the shapes RFC 4791 defines it for: the multiget
    /// and the query on the calendar and on an event (scoped to it), sync-collection on the
    /// calendar alone, expand-property on the home and the calendar, free-busy-query on the
    /// calendar alone — on an event it is the base's considered <c>403 supported-report</c>, never
    /// a 500, which a client retries on the same resource for ever.
    /// </summary>
    private protected override async Task<bool> ServeReportAsync(DavReportKind report, XDocument body,
        DavResourceContext resource, string requestHref, Trace trace,
        CancellationToken cancellationToken)
    {
        var kind = resource.Kind;
        switch (report)
        {
            case DavReportKind.CalendarMultiget when kind is DavResourceKind.Calendar or DavResourceKind.Event:
                trace.Responses = await MultigetReport.WriteAsync(Response, body, requestHref,
                    MemberSource(resource), cancellationToken);
                return true;
            case DavReportKind.CalendarQuery when kind is DavResourceKind.Calendar or DavResourceKind.Event:
                trace.Responses = await InCalendarSnapshotAsync(resource, (upTo, token) =>
                    CalendarQueryReport.WriteAsync(Response, body, requestHref, resource.Calendar!, resource.Event,
                        calendars, upTo, MemberSource(resource), Logger, token), cancellationToken);
                return true;
            case DavReportKind.FreeBusyQuery when kind is DavResourceKind.Calendar:
                // Not a multistatus: trace.Responses stays at its default, there being no
                // DAV:response element to count.
                await InCalendarSnapshotAsync(resource, async (upTo, token) =>
                {
                    await FreeBusyReport.WriteAsync(Response, body, resource.Calendar!, calendars, upTo,
                        clock, token);
                    return 0;
                }, cancellationToken);
                return true;
            case DavReportKind.ExpandProperty when kind is DavResourceKind.CalendarHome or DavResourceKind.Calendar:
                trace.Responses = await ExpandPropertyReport.WriteAsync(Response, body, resource, requestHref,
                    nested => DavPrincipalController.NestedContext(nested, resource.UserId, resource.PrincipalAddress),
                    Resolve, cancellationToken);
                return true;
            case DavReportKind.SyncCollection when kind is DavResourceKind.Calendar:
                // tokenIn BEFORE the call: the refusal path is what the field exists for.
                trace.TokenIn = DavSyncToken.ForLog(
                    body.Root!.Element(DavXml.Dav + "sync-token")?.Value);
                var source = MemberSource(resource);
                var window = await ReadSyncWindowAsync(body.Root!, resource.Calendar!, source, cancellationToken);
                var sync = await SyncCollectionReport.WriteAsync(Response, body, requestHref, DepthHeader(),
                    window, source, cancellationToken);
                trace.Responses = sync.Responses;
                trace.TokenOut = DavSyncToken.ForLog(sync.TokenOut);
                return true;
            default:
                return false;
        }
    }

    private EventMemberSource MemberSource(DavResourceContext resource) =>
        new(calendars, resource.Calendar!, resource.UserId, resource.PrincipalAddress, Resolve,
            resource.Event);

    /// <summary>
    /// The counter, then the candidates bounded to it, in one snapshot — the Depth: 1 listing's
    /// rule (the base's <c>InOneSnapshotAsync</c>), on the one report that lists a calendar's
    /// members by itself: a member ranked past a counter read beside it would be one the client's
    /// next poll never sees change. Nothing flushes inside it, so no slow client holds an InnoDB
    /// read view open. A query scoped to one event reads nothing more, and opens nothing.
    /// </summary>
    private Task<int> InCalendarSnapshotAsync(DavResourceContext resource,
        Func<ulong, CancellationToken, Task<int>> write, CancellationToken cancellationToken)
    {
        if (resource.Event is not null) return write(ulong.MaxValue, cancellationToken);

        Func<CancellationToken, Task<int>> operation = async token =>
        {
            await using var transaction = await Preferences.Database.BeginTransactionAsync(token);
            var state = await syncStore.ReadStateAsync(resource.Calendar!.Id, token);
            var responses = await write(MemberBound(state), token);
            await transaction.CommitAsync(token);
            return responses;
        };
        return Preferences.Database.CreateExecutionStrategy().ExecuteAsync(operation, cancellationToken);
    }

    /// <summary>
    /// The counter and the tombstones of one calendar, read in one snapshot that STOPS there —
    /// <c>CardDavController</c>'s reasoning, keyed by calendar. No read-or-create here: a calendar
    /// is born with its state row (5a, décision 2), so a calendar without one is a base that lost
    /// a row — refused as <c>valid-sync-token</c>, which sends the client back to an initial sync,
    /// and said out loud rather than repaired in passing.
    /// </summary>
    private async Task<SyncCollectionReport.SyncWindow> ReadSyncWindowAsync(XElement root,
        DavCalendar calendar, EventMemberSource source, CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task<SyncCollectionReport.SyncWindow?>> read = async token =>
        {
            await using var transaction = await Preferences.Database.BeginTransactionAsync(token);
            if (await syncStore.ReadStateAsync(calendar.Id, token) is not { } state) return null;
            var window = await SyncCollectionReport.ReadWindowAsync(root, state, source, token);
            await transaction.CommitAsync(token);
            return window;
        };

        if (await Preferences.Database.CreateExecutionStrategy().ExecuteAsync(read, cancellationToken)
            is { } window) return window;

        Logger.LogError("Calendar {CalendarId} ({DavName}) has no sync state row", calendar.Id, calendar.DavName);
        throw new DavPreconditionException(CalDavError.ValidSyncToken);
    }

    private protected override bool SwitchedOn() => Enabled(DavProtocol.CalDav);

    /// <summary>The two shapes that read a counter and then a list under it.</summary>
    private protected override bool NeedsSnapshot(DavResourceKind kind, DavDepthValue depth) =>
        kind is DavResourceKind.Calendar or DavResourceKind.CalendarHome
        && depth is DavDepthValue.One;

    private protected override bool IsCollection(DavResourceKind kind) =>
        kind is DavResourceKind.CalendarCollection or DavResourceKind.CalendarHome
            or DavResourceKind.Calendar;

    private protected override string HrefOf(DavResourceKind kind, Guid userId, string? collectionName,
        string? davName, string? rootHref) => kind switch
    {
        DavResourceKind.CalendarCollection => DavPaths.CalendarCollection,
        DavResourceKind.CalendarHome => DavPaths.CalendarHome(userId),
        DavResourceKind.Calendar => DavPaths.Calendar(userId, collectionName!),
        _ => DavPaths.Event(userId, collectionName!, davName!),
    };

    private protected override string? CanonicalOf(DavResourceKind kind, Guid userId,
        string? collectionName) => kind switch
    {
        DavResourceKind.CalendarCollection => DavPaths.CalendarCollection,
        DavResourceKind.CalendarHome => DavPaths.CalendarHome(userId),
        DavResourceKind.Calendar when collectionName is not null =>
            DavPaths.Calendar(userId, collectionName),
        _ => null,
    };

    /// <summary>
    /// The calendar, or the 404 an unknown segment gets — an invalid one too: a literal '/' or
    /// '\', a control character, an edge space designate nothing. Never a 400: 404 is what a client
    /// files, and it is also what says nothing about another account's collections.
    /// </summary>
    private async Task<DavCalendar?> FindCalendarOr404Async(
        string? calendarName, CancellationToken cancellationToken)
    {
        var calendar = DavName.IsValid(calendarName)
            ? await calendars.FindCalendarAsync(
                AuthenticatedUser.WebmailUid, calendarName!, cancellationToken)
            : null;
        if (calendar is null) Response.StatusCode = StatusCodes.Status404NotFound;
        return calendar;
    }

    private async Task<DavEvent?> FindEventOr404Async(
        DavCalendar calendar, string? davName, CancellationToken cancellationToken)
    {
        var member = DavName.IsValid(davName)
            ? await calendars.FindAsync(calendar.Id, davName!, cancellationToken)
            : null;
        if (member is null) Response.StatusCode = StatusCodes.Status404NotFound;
        return member;
    }

    /// <summary>Archives a refused body before its 412 leaves — only when it decodes: the storage
    /// is text, and what it cannot give back verbatim it must not pretend to keep.</summary>
    private async Task ArchiveRefusedBodyAsync(Guid userId, Guid calendarId, string davName,
        string? body, CancellationToken cancellationToken)
    {
        if (body is null) return;
        if (!await writer.ArchiveRejectedAsync(userId, calendarId, davName, body, cancellationToken))
            Logger.LogInformation("The refused PUT body for {DavName} was not archived", davName);
    }

    /// <summary>The net beneath the gate's own createOnly refusal: a Replaced that reaches here
    /// under If-None-Match: * means a write the gate should have refused — the 412 the condition
    /// earns rather than a 204 that says the create happened.</summary>
    private Task<string?> AnswerPutOutcomeAsync(DavWriteOutcome outcome, bool mustCreate,
        CancellationToken cancellationToken)
    {
        if (mustCreate && outcome.Status is DavWriteStatus.Replaced)
        {
            Response.StatusCode = StatusCodes.Status412PreconditionFailed;
            return Task.FromResult<string?>(null);
        }

        return AnswerOutcomeAsync(outcome, cancellationToken);
    }

    /// <summary>Hands the outcome to the one translator every write answer goes through, and gives
    /// back the condition it named for the log line. Nothing here decides a status: a second
    /// mapping beside <see cref="CalDavOutcomeTranslator"/> is how a branch ends up missing.</summary>
    private async Task<string?> AnswerOutcomeAsync(DavWriteOutcome outcome, CancellationToken cancellationToken)
    {
        await CalDavOutcomeTranslator.WriteAsync(Response, outcome, cancellationToken, Logger);
        return CalDavOutcomeTranslator.ConditionOf(outcome)?.LocalName;
    }
}
