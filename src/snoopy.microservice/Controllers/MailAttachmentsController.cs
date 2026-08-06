using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimeKit.Utils;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// The staged-attachment store for the composer: upload, removal, and serving a staged
/// file back to its owner. The namespace is sealed per user and account.
/// </summary>
// The route is spelled out rather than [controller]: four classes serve the historical
// api/Mail prefix, and a class-name-derived route would silently move every URL.
[Route("api/Mail")]
[ApiController]
[Authorize]
public sealed class MailAttachmentsController(
    IStagedAttachmentStore staged,
    IAccountConnectionResolver connections) : MailControllerBase(connections)
{
    /// <summary>
    /// Stages one outgoing attachment. Files upload as they are added — the Gmail/Rainloop
    /// model — and Send references the returned ids. No IMAP session is opened, but the account
    /// is still resolved: Send reads the staged namespace of the account it sends from, so a file
    /// staged under another one would simply have vanished by the time it is referenced. The body
    /// is capped at the configured message size before model binding
    /// (<see cref="AttachmentSizeLimitFilter"/>), and the store re-checks it while streaming.
    /// </summary>
    /// <param name="file">the uploaded file</param>
    /// <param name="inline">stage as a body resource (cid) rather than an attachment</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Id and metadata of the staged file</response>
    /// <response code="400">No file, a non-image staged inline, file over the limit, or account staging cap reached</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="413">The request body is over the configured message size</response>
    [HttpPost("Attachments")]
    [ServiceFilter(typeof(AttachmentSizeLimitFilter))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StagedAttachmentInfo>> UploadAttachment(
        IFormFile? file, [FromForm] bool inline, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            return BadRequestEnveloppe("A file is required");
        // Beside the file check rather than after the account resolution: both describe the
        // request itself, and neither needs a mailbox to be judged.
        if (inline && !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return BadRequestEnveloppe("An inline part must be an image");

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        await using var content = file.OpenReadStream();
        var result = await staged.SaveAsync(
            connection.StagedScope(AuthenticatedUser),
            file.FileName, file.ContentType, content, cancellationToken,
            inline ? MimeUtils.GenerateMessageId() : null);

        return FromResult(result);
    }

    /// <summary>
    /// Removes one staged attachment. 204 whether or not it existed: the namespace is sealed per
    /// user and account, so an unknown or foreign id resolves to nothing — and deleting nothing is
    /// idempotent success.
    /// </summary>
    /// <param name="id">staged attachment id</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Gone, or never was</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    [HttpDelete("Attachments/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> DeleteAttachment(Guid id, CancellationToken cancellationToken)
    {
        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        staged.Delete(connection.StagedScope(AuthenticatedUser), id);
        return NoContent();
    }

    /// <summary>
    /// Serves one staged attachment back to its owner, so the composer can display the inline
    /// images PrepareQuote staged. Always an attachment disposition plus nosniff: an img
    /// subresource renders regardless, while navigating to the URL downloads instead of
    /// rendering staged mail content on our origin.
    /// </summary>
    /// <param name="id">staged attachment id</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The staged bytes</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">Unknown id, one staged by another account, or no such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    [HttpGet("Attachments/{id:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> GetStagedAttachment(Guid id, CancellationToken cancellationToken)
    {
        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var result = staged.Open(connection.StagedScope(AuthenticatedUser), id);
        if (result.IsFailure) return NotFoundEnveloppe("Attachment not found");

        Response.Headers.XContentTypeOptions = "nosniff";
        try
        {
            var stream = System.IO.File.OpenRead(result.Value.FilePath);
            return File(stream, result.Value.Info.ContentType, result.Value.Info.FileName);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Vanished between Open and read (TTL sweep / concurrent DELETE).
            return NotFoundEnveloppe("Attachment not found");
        }
    }
}
