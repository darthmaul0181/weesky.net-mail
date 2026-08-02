using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// The composer's endpoints: sending, drafts (save and reopen) and preparing a quote for
/// reply, forward or edit-as-new.
/// </summary>
// The route is spelled out rather than [controller]: four classes serve the historical
// api/Mail prefix, and a class-name-derived route would silently move every URL.
[Route("api/Mail")]
[ApiController]
[Authorize]
public sealed class MailComposeController(
    IMailMessageRepository messages,
    IAccountConnectionResolver connections,
    IMailSender sender,
    IQuotePreparer quotes,
    IDraftSaver drafts) : MailControllerBase(connections)
{
    /// <summary>
    /// Sends a composed message: sanitised multipart/alternative body, staged attachments,
    /// then a \Seen copy APPENDed to the sent role. The SMTP envelope is derived from the
    /// recipients and MailKit strips the Bcc header at transmission, so only the addressees
    /// see it went out; the filed Sent copy keeps the header so the sender can see who was
    /// blind-copied. A failed copy never fails the send — the response says which happened.
    /// An optional fromAddress sends as one of the sending account's own addresses — the primary
    /// or a live alias on the home server, its own login address or a stored identity on a
    /// connected one; the display label is resolved server-side, never taken from the request.
    /// </summary>
    /// <param name="request">recipients, subject, HTML body, staged attachment ids and an optional fromAddress</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Sent; appendedToSent tells whether the copy was filed</response>
    /// <response code="400">No recipient, an invalid address, a fromAddress the account does not own, or a staged id no longer available</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server refused the submission</response>
    [HttpPost("Send")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<SendMessageResult>> SendMessage(SendMessageRequest request, CancellationToken cancellationToken)
    {
        // Unreachable behind model binding, which refuses first with the identical message —
        // kept as defence in depth, and as the path the direct-invocation tests exercise.
        if (request == null) return BadRequestEnveloppe("Request body is required");

        var invalid = NormalizeOutgoing(request, requireRecipient: true, out var normalized);
        if (invalid != null) return invalid;
        request = normalized;

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var result = await sender.SendAsync(AuthenticatedUser, connection, request, cancellationToken);

        if (result.IsFailure)
        {
            var refused = RefusedBuild(result.Error, request.FromAddress);
            if (refused != null) return refused;
        }

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>
    /// Prepares quoting a message for the composer: the body re-sanitised by the outgoing policy
    /// with cid images rewritten to staged-content URLs, inline parts staged, and — for forward
    /// and editAsNew — the real attachments re-staged server-side. Called on the Reply / Forward
    /// / Edit-as-new click, never on ordinary reading.
    /// </summary>
    /// <param name="request">folder, uid, and the purpose ("reply", "forward" or "editAsNew")</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The quotable body and the staged parts</response>
    /// <response code="400">Missing folder, unknown purpose, or staging over the account caps</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No message with that UID in that folder</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpPost("Messages/PrepareQuote")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PreparedQuote>> PrepareQuote(PrepareQuoteRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Folder))
            return BadRequestEnveloppe("A folder is required");

        QuotePurpose? purpose = request.Purpose switch
        {
            "reply" => QuotePurpose.Reply,
            "forward" => QuotePurpose.Forward,
            "editAsNew" => QuotePurpose.EditAsNew,
            _ => null,
        };
        if (purpose == null)
            return BadRequestEnveloppe("Purpose must be reply, forward or editAsNew");

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var message = await messages.GetMimeMessageAsync(
            AuthenticatedUser, connection, request.Folder, request.Uid, cancellationToken);
        if (message.IsFailure && IsMissing(message.Error))
            return NotFoundEnveloppe(message.Error);
        if (message.IsFailure)
            return BadGatewayEnveloppe(message.Error);

        var prepared = await quotes.PrepareAsync(
            connection.StagedScope(AuthenticatedUser), message.Value, purpose.Value, cancellationToken);

        // A failure here is the staging caps talking (file size / account quota): 400, actionable.
        if (prepared.IsFailure)
            return BadRequestEnveloppe(prepared.Error);

        return Ok(prepared.Value);
    }

    /// <summary>
    /// Saves the composer's content as a draft in the drafts-role folder (\Draft \Seen), replacing
    /// the previous version when replaceUid names one. An empty or recipient-less draft is valid;
    /// the message itself is built by the same pipeline as Send, so threading and attachments
    /// survive a save/resume round trip. Attachments live in the stored message — the staged
    /// files remain for the still-open composer and expire on their own.
    /// </summary>
    /// <param name="request">the draft content, plus the UID it replaces</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Saved; the new UID and the folder it landed in</response>
    /// <response code="400">An invalid address, a fromAddress the account does not own, or a staged id no longer available</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">No folder holds the drafts role, or the mail server refused the save</response>
    [HttpPost("Drafts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<SavedDraft>> SaveDraft(SaveDraftRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");

        // No recipient gate here: an empty or recipient-less draft is valid, unlike a send.
        var invalid = NormalizeOutgoing(request, requireRecipient: false, out var normalized);
        if (invalid != null) return invalid;
        request = (SaveDraftRequest)normalized;

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var result = await drafts.SaveAsync(AuthenticatedUser, connection, request, cancellationToken);

        if (result.IsFailure)
        {
            var refused = RefusedBuild(result.Error, request.FromAddress);
            if (refused != null) return refused;
            if (result.Error == IDraftSaver.NoDraftsFolder)
                return BadGatewayEnveloppe(
                    "This mailbox has no drafts folder. Assign the drafts role in Settings > Folders.");
        }

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>
    /// The validation Send and Drafts share. Coalesces every recipient list (an explicit
    /// "to": null in the body overrides the record's [] default and would NRE downstream),
    /// validates each address, and reduces the From to its bare address — a decorated
    /// "Name &lt;a@b.c&gt;" would never match an alias downstream and would be refused as
    /// foreign instead of accepted. Returns the 400 to answer with, or null when valid;
    /// <paramref name="normalized"/> then carries the rewritten request, its runtime type
    /// preserved — records clone virtually, so a SaveDraftRequest keeps its ReplaceUid.
    /// </summary>
    private ActionResult? NormalizeOutgoing(
        SendMessageRequest request, bool requireRecipient, out SendMessageRequest normalized)
    {
        normalized = request with
        {
            To = request.To ?? [],
            Cc = request.Cc ?? [],
            Bcc = request.Bcc ?? [],
            References = request.References ?? [],
            AttachmentIds = request.AttachmentIds ?? []
        };
        if (requireRecipient && normalized.To.Count == 0)
            return BadRequestEnveloppe("At least one recipient is required");

        foreach (var address in normalized.To.Concat(normalized.Cc).Concat(normalized.Bcc))
        {
            if (string.IsNullOrWhiteSpace(address) || !MailboxAddress.TryParse(RecipientAddressParser.Options, address, out _))
                return BadRequestEnveloppe($"\"{address}\" is not a valid email address");
        }

        if (!string.IsNullOrWhiteSpace(normalized.FromAddress))
        {
            if (!MailboxAddress.TryParse(RecipientAddressParser.Options, normalized.FromAddress, out var from))
                return BadRequestEnveloppe(
                    $"\"{normalized.FromAddress}\" is not a valid email address");
            normalized = normalized with { FromAddress = from.Address };
        }

        return null;
    }

    /// <summary>
    /// The 400s Send and Drafts share for a refused message build: an unknown staged attachment
    /// or a From the account does not own. Null for any other error — the caller keeps its own
    /// mapping and its 502 fallthrough.
    /// </summary>
    private ActionResult? RefusedBuild(string error, string? fromAddress)
    {
        if (error == IOutgoingMessageFactory.UnknownAttachment)
            return BadRequestEnveloppe(
                "An attachment is no longer available; remove it and attach it again");
        if (error == IOutgoingMessageFactory.ForbiddenFrom)
            return BadRequestEnveloppe(
                $"Sending from \"{fromAddress}\" is not allowed on this account");
        return null;
    }

    /// <summary>
    /// Reopens a saved draft for editing: envelope, outbound-sanitised body with cid images
    /// rewritten to staged-content URLs, and the message's real attachments re-staged under
    /// the calling account so the composer can offer them again. Uses the same GetMimeMessage +
    /// PrepareAsync(EditAsNew) pipeline as PrepareQuote, so a save/resume round trip behaves the
    /// same way a forward or edit-as-new does.
    /// </summary>
    /// <param name="request">the drafts-role folder and the UID of the stored draft</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The envelope, editable body and re-staged attachments</response>
    /// <response code="400">Missing folder, or staging over the account caps</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No message with that UID in that folder</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpPost("Drafts/Open")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<OpenedDraft>> OpenDraft(OpenDraftRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Folder))
            return BadRequestEnveloppe("A folder is required");

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var message = await messages.GetMimeMessageAsync(
            AuthenticatedUser, connection, request.Folder, request.Uid, cancellationToken);
        if (message.IsFailure && IsMissing(message.Error))
            return NotFoundEnveloppe(message.Error);
        if (message.IsFailure)
            return BadGatewayEnveloppe(message.Error);

        var prepared = await quotes.PrepareAsync(
            connection.StagedScope(AuthenticatedUser), message.Value, QuotePurpose.EditAsNew, cancellationToken);

        // A failure here is the staging caps talking (file size / account quota): 400, actionable.
        if (prepared.IsFailure)
            return BadRequestEnveloppe(prepared.Error);

        return Ok(ToOpenedDraft(message.Value, prepared.Value));
    }

    private static OpenedDraft ToOpenedDraft(MimeMessage message, PreparedQuote prepared) =>
        new(
            Addresses(message.To), Addresses(message.Cc), Addresses(message.Bcc),
            message.Subject ?? string.Empty,
            message.From?.Mailboxes?.FirstOrDefault()?.Address,
            prepared.QuotableHtml,
            prepared.Attachments,
            string.IsNullOrWhiteSpace(message.InReplyTo) ? null : message.InReplyTo,
            message.References?.ToList() ?? [],
            MailPriorityReader.Parse(message.Headers),
            // No HTML part is what "this draft was written as text" looks like on the wire. Only
            // the draft path reads it: a reply to a text-only original still opens an HTML composer.
            // TextBody decodes with the host's newline format, and a textarea reports LF whatever it
            // was handed — an unnormalised CR would survive in the composer's state alone and
            // desynchronise the controlled input.
            string.IsNullOrEmpty(message.HtmlBody)
                ? (message.TextBody ?? string.Empty).ReplaceLineEndings("\n")
                : null);

    private static List<string> Addresses(InternetAddressList? list) =>
        list?.Mailboxes?.Select(m => m.Address).ToList() ?? [];
}
