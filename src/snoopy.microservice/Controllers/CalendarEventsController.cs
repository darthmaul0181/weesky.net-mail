using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Calendar;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>The events of every calendar of the user — one route prefix, an explicit
/// <c>[Route]</c> because <c>[controller]</c> would give it "CalendarEvents", not "Calendar/Events"
/// (the same reason the four <c>api/Mail</c> controllers each carry one).</summary>
[Route("api/Calendar/Events")]
[ApiController]
[Authorize]
public sealed class CalendarEventsController(ICalendarEventStore store) : ApiBaseController
{
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(365.2425 * OccurrenceExpander.MaxYears);

    internal static readonly string InstanceIdRequired = "instanceId is required for this scope";

    /// <summary>Creation is the one door where <c>keepRepeat</c> cannot mean anything: there is no
    /// stored RRULE to leave alone, so accepting it would drop the rule the user chose in silence.</summary>
    internal static readonly string KeepRepeatNeedsAnEvent = "keepRepeat needs an existing event";

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
        return occurrences.IsFailure
            ? BadRequestEnveloppe(occurrences.Error)
            : Ok(new OccurrenceListResponse(occurrences.Value));
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
        return Ok(new OccurrenceListResponse(occurrences));
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
        return detail == null ? NotFoundEnveloppe(CalendarEventStore.NotFound) : Ok(EventResponse.From(detail));
    }

    /// <summary>Creates an event and answers its id.</summary>
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

        var created = await store.CreateAsync(AuthenticatedUser.WebmailUid, validated.Value, cancellationToken);
        if (created.IsFailure) return MapFailure(created.Error);

        return StatusCode(StatusCodes.Status201Created, new CreatedId(created.Value));
    }

    /// <summary>
    /// Replaces the event — the whole series, one instance, or one instance and every later one —
    /// refused when the resource moved since <paramref name="request"/>'s <c>ifHash</c> was read.
    /// </summary>
    /// <param name="id">the event's identifier</param>
    /// <param name="request">the scope, the instance it targets, and the replacement fields</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Saved</response>
    /// <response code="400">A validation refusal, a missing <c>ifHash</c>, or a narrow scope without an instance id</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such event for this user</response>
    /// <response code="409">The event changed since <c>ifHash</c> was read; reload and retry</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Update(Guid id, EventUpdateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.IfHash)) return BadRequestEnveloppe("ifHash is required");
        if (RequiresInstanceId(request.Scope) && string.IsNullOrEmpty(request.InstanceId))
            return BadRequestEnveloppe(InstanceIdRequired);

        var validated = EventRequestValidator.Validate(request);
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        var updated = await store.UpdateAsync(
            AuthenticatedUser.WebmailUid, id, request.Scope, request.InstanceId, validated.Value,
            request.IfHash, cancellationToken);

        return updated.IsSuccess ? NoContent() : MapFailure(updated.Error);
    }

    /// <summary>Deletes the whole series, one instance, or one instance and every later one.</summary>
    /// <param name="id">the event's identifier</param>
    /// <param name="scope">how much of the series to remove</param>
    /// <param name="instanceId">the targeted instance, required for <see cref="EditScope.This"/> and
    /// <see cref="EditScope.ThisAndFollowing"/></param>
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
        Guid id, EditScope scope, string? instanceId, CancellationToken cancellationToken)
    {
        if (RequiresInstanceId(scope) && string.IsNullOrEmpty(instanceId)) return BadRequestEnveloppe(InstanceIdRequired);

        var deleted = await store.DeleteAsync(AuthenticatedUser.WebmailUid, id, scope, instanceId, cancellationToken);
        return deleted.IsSuccess ? NoContent() : MapFailure(deleted.Error);
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
