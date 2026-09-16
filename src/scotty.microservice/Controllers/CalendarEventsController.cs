using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services;
using weesky.Scotty.Microservice.Services.Calendar;
using weesky.Scotty.Microservice.Services.Calendar.Scheduling;

namespace weesky.Scotty.Microservice.Controllers;

/// <summary>The events of every calendar of the user — one route prefix, an explicit
/// <c>[Route]</c> because <c>[controller]</c> would give it "CalendarEvents", not "Calendar/Events"
/// (the same reason the four <c>api/Mail</c> controllers each carry one).</summary>
[Route("api/Calendar/Events")]
[ApiController]
[Authorize]
public sealed class CalendarEventsController(
    ICalendarEventStore store, IUserAddresses addresses, IOrganizerIdentity organizer,
    IInvitationScheduler scheduler, IAccountConnectionResolver connections) : ApiBaseController
{
    /// <summary>Guests on an event somebody else organizes: only the organizer invites (décision 8).</summary>
    internal const string NotOrganizer = "not_organizer";

    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(365.2425 * OccurrenceExpander.MaxYears);

    internal static readonly string InstanceIdRequired = "instanceId is required for this scope";

    /// <summary>Creation is the one door where <c>keepRepeat</c> cannot mean anything: there is no
    /// stored RRULE to leave alone, so accepting it would drop the rule the user chose in silence.</summary>
    internal static readonly string KeepRepeatNeedsAnEvent = "keepRepeat needs an existing event";

    private Task<MailAccountConnection?>? session;

    /// <summary>Every occurrence across every calendar of the user inside <c>[from, to[</c>.</summary>
    /// <param name="from">the window's lower bound, an instant (<c>…Z</c> or with an offset)</param>
    /// <param name="to">the window's upper bound, an instant, exclusive</param>
    /// <param name="tz">the zone a floating instance is placed in</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The occurrences</response>
    /// <response code="400"><c>from</c> not before <c>to</c>, a window over five years, an unknown time zone, or a window holding too many occurrences</response>
    /// <response code="401">Not authenticated</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<OccurrenceListResponse>> Window(
        DateTimeOffset from, DateTimeOffset to, string tz, CancellationToken cancellationToken)
    {
        if (!IcsTimeZones.IsKnownIana(tz)) return BadRequestEnveloppe(IcsTimeZones.UnknownZone);

        // DateTimeOffset, never DateTime: the query-string binder reads a bare DateTime as the
        // host's own local kind, so a "…Z" value would come back shifted by the host's offset
        // before Kind is even looked at. UtcDateTime carries Kind.Utc unconditionally.
        var fromUtc = from.UtcDateTime;
        var toUtc = to.UtcDateTime;
        if (fromUtc >= toUtc) return BadRequestEnveloppe("from must be before to");
        if (toUtc - fromUtc > MaxWindow)
            return BadRequestEnveloppe($"The window cannot span more than {OccurrenceExpander.MaxYears} years");

        var occurrences = await store.WindowAsync(AuthenticatedUser.WebmailUid, fromUtc, toUtc, tz, cancellationToken);
        if (occurrences.IsFailure) return BadRequestEnveloppe(occurrences.Error);
        return Ok(await AnsweredAsync(occurrences.Value, cancellationToken));
    }

    /// <summary>Fonctionnalité 5: one result per event, at the occurrence that comes next.</summary>
    /// <param name="q">the text to search summary, location and description for</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The occurrences</response>
    /// <response code="401">Not authenticated</response>
    [HttpGet("Search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<OccurrenceListResponse>> Search(string q, CancellationToken cancellationToken)
    {
        var occurrences = await store.SearchAsync(AuthenticatedUser.WebmailUid, q ?? string.Empty, cancellationToken);
        return Ok(await AnsweredAsync(occurrences, cancellationToken));
    }

    /// <summary>One resource as the editor opens it.</summary>
    /// <param name="id">the event's identifier</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The event</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such event for this user</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var detail = await store.GetAsync(AuthenticatedUser.WebmailUid, id, cancellationToken);
        if (detail == null) return NotFoundEnveloppe(CalendarEventStore.NotFound);
        var mine = await OwnAnswersAsync([id], await OwnAddressesAsync(cancellationToken), cancellationToken);
        return Ok(EventResponse.From(detail with
        {
            MyPartStat = mine.TryGetValue(id, out var answer) ? answer : detail.MyPartStat,
            CanInvite = MayInvite(detail, await OrganizerAddressesAsync(cancellationToken)),
        }));
    }

    private Task<IReadOnlyList<string>> OwnAddressesAsync(CancellationToken cancellationToken) =>
        addresses.ForPrincipalAsync(AuthenticatedUser, cancellationToken);

    /// <summary>The addresses the user organizes under: the primary account's alone, a connected account is somebody else.</summary>
    private Task<IReadOnlyList<string>> OrganizerAddressesAsync(CancellationToken cancellationToken) =>
        addresses.ForPrimaryAsync(AuthenticatedUser, cancellationToken);

    /// <summary>The user's own answers, stamped here and not in the store: only the controller
    /// holds the principal the address list is read for (spec 5e).</summary>
    private Task<IReadOnlyDictionary<Guid, string>> OwnAnswersAsync(
        IEnumerable<Guid> eventIds, IReadOnlyList<string> own, CancellationToken cancellationToken) =>
        store.OwnPartStatsAsync(AuthenticatedUser.WebmailUid, [.. eventIds.Distinct()], own, cancellationToken);

    /// <summary>Décision 8: every ORGANIZER on any component, master included, is one of the primary
    /// account's addresses — or there is none. One foreign organizer is enough to refuse: a save with guests
    /// writes the user's ORGANIZER on every component, over that one too.
    /// <paramref name="own"/> comes lower-cased from <see cref="IUserAddresses.ForPrimaryAsync"/>.</summary>
    private static bool MayInvite(EventDetail detail, IReadOnlyList<string> own) =>
        detail.Attendees.Where(a => a.IsOrganizer)
            .All(host => own.Contains(host.Email.Trim().ToLowerInvariant(), StringComparer.Ordinal));

    /// <summary>The ORGANIZER goes with the guests: resolved only when there are some.</summary>
    private async Task<EventWrite> WithOrganizerAsync(EventWrite write, CancellationToken cancellationToken) =>
        write.Attendees is { Count: > 0 }
            ? write with { Organizer = await organizer.ResolveAsync(AuthenticatedUser, cancellationToken) }
            : write;

    private async Task<OccurrenceListResponse> AnsweredAsync(
        IReadOnlyList<EventOccurrence> occurrences, CancellationToken cancellationToken)
    {
        var mine = await OwnAnswersAsync(occurrences.Select(o => o.EventId), await OwnAddressesAsync(cancellationToken), cancellationToken);
        return new OccurrenceListResponse([.. occurrences
            .Select(o => mine.TryGetValue(o.EventId, out var answer) ? o with { MyPartStat = answer } : o)]);
    }

    /// <summary>Creates an event and answers its id, with what the invitation hook sent.</summary>
    /// <param name="request">the event to create</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="201">Created</response>
    /// <response code="400">A validation refusal, a <c>keepRepeat</c> that has no event to keep, or the calendar's cap reached</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such calendar for this user</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CreatedId>> Create(EventRequest request, CancellationToken cancellationToken)
    {
        if (request is { KeepRepeat: true }) return BadRequestEnveloppe(KeepRepeatNeedsAnEvent);

        var validated = EventRequestValidator.Validate(request);
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        var write = await WithOrganizerAsync(validated.Value, cancellationToken);
        var created = await store.CreateAsync(AuthenticatedUser.WebmailUid, write, cancellationToken);
        if (created.IsFailure) return MapFailure(created.Error);

        var report = await ScheduleAsync(created.Value, request.Language, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new CreatedId(created.Value.EventId, report));
    }

    /// <summary>
    /// Replaces the event — the whole series, one instance, or one instance and every later one —
    /// refused when the resource moved since <paramref name="request"/>'s <c>ifHash</c> was read.
    /// </summary>
    /// <param name="id">the event's identifier</param>
    /// <param name="request">the scope, the instance it targets, and the replacement fields</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Saved; <c>scheduling</c> says what was sent</response>
    /// <response code="400">A validation refusal, a missing <c>ifHash</c>, a narrow scope without an instance id, or <c>attendees</c> on an event somebody else organizes (<c>not_organizer</c>)</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such event for this user</response>
    /// <response code="409">The event changed since <c>ifHash</c> was read; reload and retry</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(EventUpdated), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Update(Guid id, EventUpdateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.IfHash)) return BadRequestEnveloppe("ifHash is required");
        if (RequiresInstanceId(request.Scope) && string.IsNullOrEmpty(request.InstanceId))
            return BadRequestEnveloppe(InstanceIdRequired);

        EventDetail? current = null;
        if (request.Attendees is not null)
        {
            current = await store.GetAsync(AuthenticatedUser.WebmailUid, id, cancellationToken);
            if (current is null) return MapFailure(CalendarEventStore.NotFound);
            if (!MayInvite(current, await OrganizerAddressesAsync(cancellationToken))) return BadRequestEnveloppe(NotOrganizer);
        }

        var kept = current?.Attendees.Where(a => a.RecurrenceId is null && !a.IsOrganizer).Select(a => a.Email);
        var validated = EventRequestValidator.Validate(request, kept);
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        var write = await WithOrganizerAsync(validated.Value, cancellationToken);
        var updated = await store.UpdateAsync(
            AuthenticatedUser.WebmailUid, id, request.Scope, request.InstanceId, write,
            request.IfHash, cancellationToken);

        return updated.IsSuccess
            ? Ok(new EventUpdated(await ScheduleAsync(updated.Value, request.Language, cancellationToken)))
            : MapFailure(updated.Error);
    }

    /// <summary>Deletes the whole series, one instance, or one instance and every later one.</summary>
    /// <param name="id">the event's identifier</param>
    /// <param name="scope">how much of the series to remove</param>
    /// <param name="instanceId">the targeted instance, required for <see cref="EditScope.This"/> and
    /// <see cref="EditScope.ThisAndFollowing"/></param>
    /// <param name="language">the language of the cancellations this removal may send: "fr" or "en" (the default)</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Deleted (or nothing changed: the narrow scope named nothing to remove)</response>
    /// <response code="400">A narrow scope without an instance id</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such event for this user</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(
        Guid id, EditScope scope, string? instanceId, [FromQuery] string? language, CancellationToken cancellationToken)
    {
        if (RequiresInstanceId(scope) && string.IsNullOrEmpty(instanceId)) return BadRequestEnveloppe(InstanceIdRequired);

        var deleted = await store.DeleteAsync(AuthenticatedUser.WebmailUid, id, scope, instanceId, cancellationToken);
        if (deleted.IsFailure) return MapFailure(deleted.Error);

        await ScheduleAsync(deleted.Value, language ?? "en", cancellationToken);
        return NoContent();
    }

    /// <summary>The hook once per resource the write touched — a split touches two — summed into one report.</summary>
    private async Task<SchedulingReport> ScheduleAsync(EventWriteResult written, string language, CancellationToken cancellationToken)
    {
        string? owner = null;
        var sent = 0;
        foreach (var change in written.Changes)
        {
            var report = await scheduler.AfterWriteAsync(AuthenticatedUser, change, WriteOrigin.Webmail, OpenSessionAsync, language, cancellationToken);
            sent += report.Sent;
            owner ??= report.Owner;
        }
        return new SchedulingReport(owner, sent);
    }

    /// <summary>The user's own SMTP session, resolved only when a mail is due and once per request: the
    /// primary account's, since the organizer is always its identity (décisions 8 and 10). Null — the
    /// queue takes over — when the cookie carries no credentials or the request names another account.</summary>
    private Task<MailAccountConnection?> OpenSessionAsync(CancellationToken cancellationToken) =>
        session ??= ResolveSessionAsync(cancellationToken);

    private async Task<MailAccountConnection?> ResolveSessionAsync(CancellationToken cancellationToken)
    {
        var resolved = await connections.ResolveAsync(AuthenticatedUser, Request, cancellationToken);
        return resolved.IsSuccess && resolved.Value.AccountId == MailAccountConnection.Primary ? resolved.Value : null;
    }

    private static bool RequiresInstanceId(EditScope scope) =>
        scope is EditScope.This or EditScope.ThisAndFollowing;

    /// <summary>The one mapping every write door of this controller shares: a missing row is 404, a
    /// resource that moved under an <c>ifHash</c> is 409, anything else is a rejected body.</summary>
    private ActionResult MapFailure(string error) => error switch
    {
        CalendarEventStore.NotFound or CalendarStore.NotFound => NotFoundEnveloppe(error),
        CalendarEventStore.EventMoved => ConflictEnveloppe(error),
        _ => BadRequestEnveloppe(error),
    };
}
