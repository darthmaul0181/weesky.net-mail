using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Calendar;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>The user's calendars — webmail data, not a DAV collection. No IMAP session and no
/// credentials cookie: every action is a database read or write, the shape ContactsController is.</summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class CalendarsController(ICalendarStore store, ICalendarEventStore events) : ApiBaseController
{
    // What a file picker names a .ics attachment, plus what a browser sends when it recognises
    // none of the above (Contacts.Import faces the same gap for .vcf).
    private static readonly string[] VCalendarMediaTypes = ["text/calendar", "application/ics", "application/octet-stream"];

    private const int MaxReportedErrors = 50;

    internal static readonly string NeedsDisplayName = "A calendar needs a display name";

    /// <summary>Every calendar of the user, the <c>default</c> one created with <paramref name="tz"/>
    /// the first time it is asked for (décision 6).</summary>
    /// <param name="tz">the browser's IANA time zone</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The calendars</response>
    /// <response code="400">Unknown time zone</response>
    /// <response code="401">Not authenticated</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CalendarListResponse>> List(string tz, CancellationToken cancellationToken)
    {
        if (!IcsTimeZones.IsKnownIana(tz)) return BadRequestEnveloppe(IcsTimeZones.UnknownZone);

        await store.EnsureDefaultAsync(AuthenticatedUser.WebmailUid, tz, cancellationToken);
        var calendars = await store.ListAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        return Ok(new CalendarListResponse(calendars));
    }

    /// <summary>Creates a calendar, its colour the palette's next and its rank the last.</summary>
    /// <param name="tz">the new calendar's own time zone</param>
    /// <param name="request">the calendar to create</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="201">Created</response>
    /// <response code="400">No display name, an unknown time zone, an invalid colour, or the cap reached</response>
    /// <response code="401">Not authenticated</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CreatedId>> Create(
        string tz, CalendarRequest request, CancellationToken cancellationToken)
    {
        if (!IcsTimeZones.IsKnownIana(tz)) return BadRequestEnveloppe(IcsTimeZones.UnknownZone);
        if (string.IsNullOrWhiteSpace(request.DisplayName)) return BadRequestEnveloppe(NeedsDisplayName);

        var created = await store.CreateAsync(
            AuthenticatedUser.WebmailUid,
            new CalendarWrite(request.DisplayName.Trim(), request.Description, request.Color, request.Order),
            tz, cancellationToken);
        if (created.IsFailure) return BadRequestEnveloppe(created.Error);

        return StatusCode(StatusCodes.Status201Created, new CreatedId(created.Value));
    }

    /// <summary>Replaces the name, the description, the colour and the rank.</summary>
    /// <param name="id">the calendar's identifier</param>
    /// <param name="request">the full replacement</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Saved</response>
    /// <response code="400">No display name, or an invalid colour</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such calendar for this user</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Update(Guid id, CalendarRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName)) return BadRequestEnveloppe(NeedsDisplayName);

        var updated = await store.UpdateAsync(
            AuthenticatedUser.WebmailUid, id,
            new CalendarWrite(request.DisplayName.Trim(), request.Description, request.Color, request.Order),
            cancellationToken);

        return updated.IsSuccess ? NoContent() : MapFailure(updated.Error);
    }

    /// <summary>The sidebar checkbox alone, never projected to DAV (décision 2).</summary>
    /// <param name="id">the calendar's identifier</param>
    /// <param name="request">the new visibility</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Saved</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such calendar for this user</response>
    [HttpPut("{id:guid}/Visible")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SetVisible(
        Guid id, CalendarVisibleRequest request, CancellationToken cancellationToken)
    {
        var result = await store.SetVisibleAsync(AuthenticatedUser.WebmailUid, id, request.Visible, cancellationToken);
        return result.IsSuccess ? NoContent() : MapFailure(result.Error);
    }

    /// <summary>Removes the calendar and archives every event it held. The <c>default</c> calendar
    /// is refused: a user with none has nowhere left to write.</summary>
    /// <param name="id">the calendar's identifier</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Deleted</response>
    /// <response code="400">The default calendar cannot be deleted</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such calendar for this user</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await store.DeleteAsync(AuthenticatedUser.WebmailUid, id, cancellationToken);
        return result.IsSuccess ? NoContent() : MapFailure(result.Error);
    }

    /// <summary>The whole collection as one VCALENDAR — its own events, its VTIMEZONEs written once
    /// each, and its name and colour.</summary>
    /// <param name="id">the calendar's identifier</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The file</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such calendar for this user</response>
    [HttpGet("{id:guid}/Export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Export(Guid id, CancellationToken cancellationToken)
    {
        var calendars = await store.ListAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        if (calendars.FirstOrDefault(c => c.Id == id) is not { } calendar) return NotFoundEnveloppe(CalendarStore.NotFound);

        var ics = await events.ExportAsync(AuthenticatedUser.WebmailUid, id, cancellationToken);
        return File(Encoding.UTF8.GetBytes(ics), "text/calendar",
            $"{Slug(calendar.DisplayName)}-{DateTime.UtcNow:yyyy-MM-dd}.ics");
    }

    /// <summary>
    /// Merges a VCALENDAR file into the collection, grouped by UID: an existing one is replaced
    /// whole, VTODO and VJOURNAL are counted and never stored (fonctionnalité 6).
    /// </summary>
    /// <param name="id">the calendar's identifier</param>
    /// <param name="file">the .ics file</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The report</response>
    /// <response code="400">No file, or a media type no calendar client writes</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such calendar for this user</response>
    [HttpPost("{id:guid}/Import")]
    [RequestSizeLimit(CalendarEventStore.MaxImportBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CalendarImportReport>> Import(
        Guid id, IFormFile? file, CancellationToken cancellationToken)
    {
        var accepted = CheckCalendarFile(file);
        if (accepted.IsFailure) return BadRequestEnveloppe(accepted.Error);

        var calendars = await store.ListAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        if (calendars.All(c => c.Id != id)) return NotFoundEnveloppe(CalendarStore.NotFound);

        var vcalendar = await ReadCalendarFileAsync(file!, cancellationToken);
        return Ok(Report(await events.ImportAsync(AuthenticatedUser.WebmailUid, id, vcalendar, cancellationToken)));
    }

    /// <summary>
    /// Creates a calendar and pours the file straight into it, so importing somebody else's agenda
    /// is one gesture. Nothing is created when the file is not one we accept.
    /// </summary>
    /// <param name="tz">the new calendar's own time zone</param>
    /// <param name="displayName">the new calendar's name</param>
    /// <param name="color">its colour; the palette's next one when absent</param>
    /// <param name="file">the .ics file</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="201">The calendar and the report</response>
    /// <response code="400">No file, a media type no calendar client writes, no display name, an
    /// unknown time zone, an invalid colour, or the cap reached</response>
    /// <response code="401">Not authenticated</response>
    [HttpPost("Import")]
    [RequestSizeLimit(CalendarEventStore.MaxImportBytes)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CalendarImportResponse>> ImportAsNew(
        string tz, [FromForm] string? displayName, [FromForm] string? color, IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!IcsTimeZones.IsKnownIana(tz)) return BadRequestEnveloppe(IcsTimeZones.UnknownZone);
        if (string.IsNullOrWhiteSpace(displayName)) return BadRequestEnveloppe(NeedsDisplayName);

        var accepted = CheckCalendarFile(file);
        if (accepted.IsFailure) return BadRequestEnveloppe(accepted.Error);

        // The cap is answered before the file is read: a refused import must not cost twenty
        // megabytes of decoding first.
        var created = await store.CreateAsync(
            AuthenticatedUser.WebmailUid, new CalendarWrite(displayName.Trim(), null, color, null),
            tz, cancellationToken);
        if (created.IsFailure) return BadRequestEnveloppe(created.Error);

        var vcalendar = await ReadCalendarFileAsync(file!, cancellationToken);
        var outcome = await events.ImportAsync(AuthenticatedUser.WebmailUid, created.Value, vcalendar, cancellationToken);

        var calendars = await store.ListAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        if (calendars.FirstOrDefault(c => c.Id == created.Value) is not { } view)
            return NotFoundEnveloppe(CalendarStore.NotFound);

        return StatusCode(StatusCodes.Status201Created, new CalendarImportResponse(view, Report(outcome)));
    }

    /// <summary>The gate both import doors pass: a body, and a media type a calendar client writes.
    /// Apart from the reading so that each door may check what it owns where it must.</summary>
    private static Result CheckCalendarFile(IFormFile? file)
    {
        if (file == null || file.Length == 0) return Result.Failure("A file is required");

        var mediaType = file.Headers?.ContentType.ToString() ?? string.Empty;
        return VCalendarMediaTypes.Any(t => mediaType.StartsWith(t, StringComparison.OrdinalIgnoreCase))
            ? Result.Success()
            : Result.Failure($"'{mediaType}' is not a calendar file");
    }

    private static async Task<string> ReadCalendarFileAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static CalendarImportReport Report(CalendarImportOutcome outcome) =>
        new(outcome.Created, outcome.Replaced, outcome.IgnoredTodos, outcome.IgnoredJournals, outcome.Failed,
            outcome.Errors.Count, outcome.Errors.OrderBy(e => e.Line).Take(MaxReportedErrors).ToList());

    /// <summary>The one mapping every write door of this controller shares: a missing row is 404,
    /// anything else — a bad colour, the default calendar, the cap — is a rejected body.</summary>
    private ActionResult MapFailure(string error) =>
        error == CalendarStore.NotFound ? NotFoundEnveloppe(error) : BadRequestEnveloppe(error);

    /// <summary>Letters, digits and dashes, lowercased; <c>"calendar"</c> when nothing survives.</summary>
    private static string Slug(string displayName)
    {
        var kept = new string([.. displayName.Where(c => char.IsLetterOrDigit(c) || c == '-')]).ToLowerInvariant();
        return kept.Length > 0 ? kept : "calendar";
    }
}
