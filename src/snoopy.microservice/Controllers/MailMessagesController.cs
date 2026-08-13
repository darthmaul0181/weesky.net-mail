using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// Reading and manipulating messages: listing, detail, source, attachments, flags,
/// move/copy/delete and search.
/// </summary>
// The route is spelled out rather than [controller]: four classes serve the historical
// api/Mail prefix, and a class-name-derived route would silently move every URL.
[Route("api/Mail")]
[ApiController]
[Authorize]
public sealed class MailMessagesController(
    IMailMessageRepository messages,
    IAccountConnectionResolver connections,
    ITrustedSenderStore trustedSenders,
    ILogger<MailMessagesController> logger) : MailControllerBase(connections)
{
    /// <summary>
    /// How much of a message the source view may carry. Internal, never a setting: headers sit
    /// at the head of the file, so what a cap drops is the tail of the base64 — and a message
    /// may legitimately weigh MailOptions.MaxMessageSizeMb (25 MB), which no browser wants
    /// dropped into a &lt;pre&gt;.
    /// </summary>
    private const int MaxSourceBytes = 1024 * 1024;

    /// <summary>
    /// One page of a folder, newest message first. The folder path travels in the query
    /// string rather than a route segment because the hierarchy separator may be '/'.
    /// </summary>
    /// <param name="folder">full folder path</param>
    /// <param name="page">zero-based page index</param>
    /// <param name="pageSize">messages per page, 1 to 200</param>
    /// <param name="grouped">group the page into conversations (server THREAD permitting)</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The page, with the folder's UidValidity</response>
    /// <response code="400">The folder is missing, or the paging arguments are out of range</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpGet("Messages")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<MailFolderPage>> GetMessages(
        [FromQuery] string folder,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 50,
        [FromQuery] bool grouped = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folder)) return BadRequestEnveloppe("A folder is required");
        if (page < 0) return BadRequestEnveloppe("Page must not be negative");

        // An unbounded page size lets one request pull an entire mailbox.
        if (pageSize is < 1 or > 200) return BadRequestEnveloppe("Page size must be between 1 and 200");

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var result = await messages.ListAsync(AuthenticatedUser, connection, folder, page, pageSize, grouped, cancellationToken);

        if (result.IsFailure && IsMissing(result.Error)) return NotFoundEnveloppe(result.Error);

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>
    /// A single message: sanitised HTML body, plain-text body, headers and attachment list.
    /// Remote images are withheld and counted, so the client can offer to load them.
    /// </summary>
    /// <param name="folder">full folder path</param>
    /// <param name="uid">message UID, valid only for the folder's current UidValidity</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The message</response>
    /// <response code="400">The folder is missing</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No message with that UID in that folder</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpGet("Messages/Detail")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<MailMessageDetail>> GetMessage(
        [FromQuery] string folder,
        [FromQuery] uint uid,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folder)) return BadRequestEnveloppe("A folder is required");

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var result = await messages.GetAsync(AuthenticatedUser, connection, folder, uid, cancellationToken);

        if (result.IsFailure && IsMissing(result.Error))
        {
            return NotFoundEnveloppe(result.Error);
        }

        if (result.IsSuccess)
        {
            await RecordSenderUseAsync(result.Value.FromAddress, cancellationToken);
        }

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>
    /// Keeps an approved sender's entry alive while it is still earning its place. Does nothing
    /// for a sender nobody approved, and never fails the read: bookkeeping degrades, it does not
    /// take the caller's message down with it.
    /// </summary>
    private async Task RecordSenderUseAsync(string? fromAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fromAddress)) return;

        try
        {
            await trustedSenders.TouchAsync(AuthenticatedUser.WebmailUid, fromAddress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Switching messages quickly aborts the read; that is routine, not a failure.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record the trusted-sender use for {Address}", fromAddress);
        }
    }

    /// <summary>
    /// Downloads one attachment. Always served as an attachment disposition: message
    /// content must never render inline in the browser.
    /// </summary>
    /// <param name="folder">full folder path</param>
    /// <param name="uid">message UID</param>
    /// <param name="part">MIME part specifier, taken from the message's attachment list. Empty
    /// is a real specifier — a message whose whole body is the attachment has no multipart
    /// wrapper to number, so it must not be validated away</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The attachment bytes</response>
    /// <response code="400">The folder is missing</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such message, or no such part on it</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpGet("Messages/Attachment")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> GetAttachment(
        [FromQuery] string folder,
        [FromQuery] uint uid,
        [FromQuery] string? part,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folder)) return BadRequestEnveloppe("A folder is required");

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var result = await messages.GetAttachmentAsync(
            AuthenticatedUser, connection, folder, uid, part ?? string.Empty, cancellationToken);

        if (result.IsFailure)
        {
            var status = IsMissing(result.Error)
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status502BadGateway;

            return StatusCode(status, ResultEnveloppe.CreateErrorEnveloppe(result.Error));
        }

        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    /// <summary>
    /// The message as it arrived: the headers worth distilling plus the verbatim RFC822 bytes,
    /// capped at one megabyte.
    /// </summary>
    /// <param name="folder">full folder path</param>
    /// <param name="uid">message UID, valid only for the folder's current UidValidity</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The source</response>
    /// <response code="400">The folder is missing</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No message with that UID in that folder</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpGet("Messages/Source")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<MailMessageSource>> GetMessageSource(
        [FromQuery] string folder,
        [FromQuery] uint uid,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folder)) return BadRequestEnveloppe("A folder is required");

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var result = await messages.GetSourceAsync(
            AuthenticatedUser, connection, folder, uid, MaxSourceBytes, cancellationToken);

        if (result.IsFailure && IsMissing(result.Error))
        {
            return NotFoundEnveloppe(result.Error);
        }

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>
    /// Sets or clears one flag on a batch of messages. A UID that no longer exists is a
    /// silent no-op: the batch never fails partially.
    /// </summary>
    /// <param name="request">folder, UIDs, the flag and the value to write</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">The flags were written</response>
    /// <response code="400">The folder is missing, or the batch is empty or above 200 UIDs</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpPut("Messages/Flags")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> SetMessageFlags(SetMessageFlagsRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequestEnveloppe("A folder is required");
        if (request.Uids.Count is < 1 or > 200) return BadRequestEnveloppe("Uids must hold between 1 and 200 entries");

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var result = await messages.SetFlagsAsync(
            AuthenticatedUser, connection, request.FolderPath, request.Uids, request.Flag, request.Value, cancellationToken);

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway, successStatusCode: StatusCodes.Status204NoContent);
    }

    /// <summary>Moves a batch of messages into another folder.</summary>
    /// <param name="request">source folder, UIDs and target folder</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">The messages were moved</response>
    /// <response code="400">A folder is missing, the batch is empty or above 200 UIDs, the target equals the source, or the target cannot hold messages</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpPost("Messages/Move")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> MoveMessages(MoveMessagesRequest request, CancellationToken cancellationToken)
        => MoveOrCopy(request, copy: false, cancellationToken);

    /// <summary>Copies a batch of messages into another folder.</summary>
    /// <param name="request">source folder, UIDs and target folder</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">The messages were copied</response>
    /// <response code="400">A folder is missing, the batch is empty or above 200 UIDs, the target equals the source, or the target cannot hold messages</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpPost("Messages/Copy")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> CopyMessages(MoveMessagesRequest request, CancellationToken cancellationToken)
        => MoveOrCopy(request, copy: true, cancellationToken);

    private async Task<ActionResult> MoveOrCopy(MoveMessagesRequest request, bool copy, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequestEnveloppe("A folder is required");
        if (request.Uids.Count is < 1 or > 200) return BadRequestEnveloppe("Uids must hold between 1 and 200 entries");
        if (string.IsNullOrWhiteSpace(request.TargetFolderPath)) return BadRequestEnveloppe("A target folder is required");
        if (string.Equals(request.FolderPath, request.TargetFolderPath, StringComparison.Ordinal))
            return BadRequestEnveloppe("The target folder must differ from the source folder");

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var result = await messages.MoveOrCopyAsync(
            AuthenticatedUser, connection, request.FolderPath, request.Uids, request.TargetFolderPath, copy, cancellationToken);

        if (result.IsFailure && result.Error == ImapSession.TargetNotSelectable)
            return BadRequestEnveloppe("The target folder cannot hold messages");

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway, successStatusCode: StatusCodes.Status204NoContent);
    }

    /// <summary>
    /// Permanently deletes a batch of messages via UID EXPUNGE, bypassing the trash entirely.
    /// </summary>
    /// <param name="request">folder and UIDs</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">The messages were deleted</response>
    /// <response code="400">The folder is missing, or the batch is empty or above 200 UIDs</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server could not be reached, or cannot delete without UIDPLUS</response>
    [HttpDelete("Messages")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> DeleteMessages(DeleteMessagesRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequestEnveloppe("A folder is required");
        if (request.Uids.Count is < 1 or > 200) return BadRequestEnveloppe("Uids must hold between 1 and 200 entries");

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var result = await messages.DeleteAsync(AuthenticatedUser, connection, request.FolderPath, request.Uids, cancellationToken);

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway, successStatusCode: StatusCodes.Status204NoContent);
    }

    /// <summary>
    /// One page of search results, newest first. Criteria combine with AND; Quick is the
    /// fast bar and means subject OR sender. AllFolders sweeps every selectable folder in
    /// one session. Paths travel in the body, never in a route segment.
    /// </summary>
    /// <param name="request">criteria, scope and paging</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The page of results</response>
    /// <response code="400">The folder is missing, no criterion is filled, or the paging arguments are out of range</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpPost("Messages/Search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<MailSearchPage>> SearchMessages(SearchMessagesRequest request, CancellationToken cancellationToken)
    {
        // Unreachable behind model binding, which refuses first with the identical message —
        // kept as defence in depth, and as the path the direct-invocation tests exercise.
        if (request == null) return BadRequestEnveloppe("Request body is required");
        if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequestEnveloppe("A folder is required");
        if (request.Page < 0) return BadRequestEnveloppe("Page must not be negative");
        if (request.PageSize is < 1 or > 200) return BadRequestEnveloppe("Page size must be between 1 and 200");

        var criteria = new MailSearchCriteria(
            request.Quick, request.From, request.To, request.Subject, request.Text,
            request.SinceDays, request.Unread, request.Flagged, request.HasAttachment);
        if (!MailSearchQueryBuilder.HasAnyCriterion(criteria))
            return BadRequestEnveloppe("At least one search criterion is required");

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var result = await messages.SearchAsync(
            AuthenticatedUser, connection, request.FolderPath, request.AllFolders,
            criteria, request.Page, request.PageSize, cancellationToken);

        if (result.IsFailure && IsMissing(result.Error)) return NotFoundEnveloppe(result.Error);

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }
}
