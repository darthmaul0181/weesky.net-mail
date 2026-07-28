using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// Mail access over IMAP.
///
/// Two conventions hold across every action. Folder paths never appear in a route
/// segment — the hierarchy separator may be '/', which would break routing — so they
/// travel in the query string or the request body. And the two failure modes are
/// distinct: a missing or undecryptable credentials cookie is 401 with the code
/// "credentials_unavailable" so the client can sign in again, while anything the mail
/// server refuses is 502.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class MailController(
    IMailFolderRepository folders,
    IMailMessageRepository messages,
    IMailCredentialStore credentials,
    IFolderRoleStore roleStore,
    IStagedAttachmentStore staged,
    IMailSender sender,
    IQuotePreparer quotes,
    IDraftSaver drafts,
    ITrustedSenderStore trustedSenders,
    ILogger<MailController> logger) : ApiBaseController
{
    /// <summary>
    /// The caller's mail password, decrypted from the credentials cookie, or false with the
    /// <paramref name="unauthorized"/> to answer with.
    ///
    /// Every mail action opens IMAP as the user, so every one of them starts here. A guard
    /// rather than an action filter on purpose: the controller tests invoke actions directly
    /// and no filter would run, so moving the check into the pipeline would move it out of
    /// the tests that cover it.
    /// </summary>
    private bool TryMailPassword(
        [NotNullWhen(true)] out string? password,
        [NotNullWhen(false)] out ActionResult? unauthorized)
    {
        var retrieved = credentials.Retrieve(Request);

        password = retrieved.IsSuccess ? retrieved.Value : null;
        unauthorized = retrieved.IsSuccess ? null : UnauthorizedEnveloppe(retrieved.Error);

        return retrieved.IsSuccess;
    }

    /// <summary>
    /// Refuses an operation on a folder holding a well-known role, and returns null when it
    /// may proceed. Renaming or deleting one breaks the role for every client on the mailbox;
    /// hiding one strands whatever gets filed into it.
    /// </summary>
    /// <param name="password">the caller's mail password</param>
    /// <param name="path">folder the operation targets</param>
    /// <param name="verb">what the caller is trying to do, for the message</param>
    /// <param name="includeDescendants">
    /// True for deletion: removing a parent takes its children with it, so a guard on the
    /// target path alone could be stepped around one level up.
    /// </param>
    /// <param name="cancellationToken">cancellation token</param>
    private async Task<ActionResult?> RefuseIfSystemFolderAsync(
        string password, string path, string verb, bool includeDescendants, CancellationToken cancellationToken)
    {
        var tree = await folders.GetTreeAsync(AuthenticatedUser, password, cancellationToken);
        if (tree.IsFailure)
            return BadGatewayEnveloppe(tree.Error);

        var overrides = await roleStore.GetAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        var roleByPath = FolderRoleResolver.Resolve(tree.Value, overrides).RoleByPath;

        if (roleByPath.TryGetValue(path, out var role))
            return BadRequestEnveloppe(
                $"This folder is the {role} folder and cannot be {verb}. Point {role} at another folder first.");

        if (includeDescendants && tree.Value.FindByPath(path) is { } target)
        {
            foreach (var descendant in target.Descendants())
            {
                if (roleByPath.TryGetValue(descendant.Path, out var childRole))
                    return BadRequestEnveloppe(
                        $"\"{descendant.Name}\" inside this folder is the {childRole} folder, so deleting it would take {childRole} with it. Point {childRole} at another folder first.");
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the caller's folder tree: hierarchy, well-known roles, subscription state
    /// and message counts.
    /// </summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The folder tree</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpGet("Folders")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<MailFolderNode>>> GetFolders(CancellationToken cancellationToken)
    {
        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        var result = await folders.GetTreeAsync(AuthenticatedUser, password, cancellationToken);
        if (result.IsFailure)
            return BadGatewayEnveloppe(result.Error);

        // The tree's SpecialUse is the resolution chain's output, not raw discovery: a
        // user override reassigns the role, and the displaced folder shows under its own
        // name (spec § 4.1).
        var overrides = await roleStore.GetAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        var resolution = FolderRoleResolver.Resolve(result.Value, overrides);
        StampRoles(result.Value, resolution.RoleByPath);

        return Ok(result.Value);
    }

    /// <summary>
    /// Creates a folder and subscribes it, so it appears without a further step.
    /// </summary>
    /// <param name="request">parent path and leaf name</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Full path of the new folder</response>
    /// <response code="400">The request body or the folder name is missing</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="502">The mail server refused the operation</response>
    [HttpPost("Folders")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<string>> CreateFolder(CreateFolderRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequestEnveloppe("A folder name is required");

        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        var result = await folders.CreateFolderAsync(
            AuthenticatedUser, password, request.ParentPath ?? string.Empty, request.Name, cancellationToken);

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>Renames a folder, optionally moving it under a different parent.</summary>
    /// <param name="request">current path, new parent path and new leaf name</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Path of the renamed folder</response>
    /// <response code="400">The request body, the path or the new name is missing</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="502">The mail server refused the operation</response>
    [HttpPut("Folders")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<string>> RenameFolder(RenameFolderRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");
        if (string.IsNullOrWhiteSpace(request.Path)) return BadRequestEnveloppe("A folder path is required");
        if (string.IsNullOrWhiteSpace(request.NewName)) return BadRequestEnveloppe("A folder name is required");

        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        if (await RefuseIfSystemFolderAsync(
                password, request.Path, "renamed", includeDescendants: false, cancellationToken) is { } refusal)
            return refusal;

        var result = await folders.RenameFolderAsync(
            AuthenticatedUser, password, request.Path, request.NewParentPath ?? string.Empty, request.NewName, cancellationToken);

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>Deletes a folder. The inbox cannot be deleted.</summary>
    /// <param name="request">path of the folder to delete</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Folder deleted</response>
    /// <response code="400">The request body or the path is missing</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="502">The mail server refused the operation</response>
    [HttpDelete("Folders")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> DeleteFolder(DeleteFolderRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");
        if (string.IsNullOrWhiteSpace(request.Path)) return BadRequestEnveloppe("A folder path is required");

        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        if (await RefuseIfSystemFolderAsync(
                password, request.Path, "deleted", includeDescendants: true, cancellationToken) is { } refusal)
            return refusal;

        var result = await folders.DeleteFolderAsync(AuthenticatedUser, password, request.Path, cancellationToken);

        return FromResult(result,
            errorStatusCode: StatusCodes.Status502BadGateway,
            successStatusCode: StatusCodes.Status204NoContent);
    }

    /// <summary>
    /// Subscribes or unsubscribes a folder. This is how a folder is hidden from the
    /// folder list without deleting it.
    /// </summary>
    /// <param name="request">folder path and desired subscription state</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Visibility changed</response>
    /// <response code="400">The request body or the path is missing</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="502">The mail server refused the operation</response>
    [HttpPut("Folders/Subscription")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> SetFolderSubscription(FolderSubscriptionRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");
        if (string.IsNullOrWhiteSpace(request.Path)) return BadRequestEnveloppe("A folder path is required");

        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        // Only hiding is refused: refusing to subscribe would leave a mailbox whose trash
        // another client hid stuck that way.
        if (!request.Subscribed && await RefuseIfSystemFolderAsync(
                password, request.Path, "hidden", includeDescendants: false, cancellationToken) is { } refusal)
            return refusal;

        var result = await folders.SetSubscriptionAsync(
            AuthenticatedUser, password, request.Path, request.Subscribed, cancellationToken);

        return FromResult(result,
            errorStatusCode: StatusCodes.Status502BadGateway,
            successStatusCode: StatusCodes.Status204NoContent);
    }

    /// <summary>
    /// The five assignable roles, each with what it resolves to and why: the user's
    /// override, a server SPECIAL-USE flag, or a name match. A stale override — its folder
    /// renamed or deleted outside this app — is signalled alongside whatever discovery
    /// now yields; it is kept, never auto-deleted (spec § 5.3).
    /// </summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The five roles</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpGet("FolderRoles")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<FolderRoleEntry>>> GetFolderRoles(CancellationToken cancellationToken)
    {
        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        var tree = await folders.GetTreeAsync(AuthenticatedUser, password, cancellationToken);
        if (tree.IsFailure)
            return BadGatewayEnveloppe(tree.Error);

        var overrides = await roleStore.GetAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        var resolution = FolderRoleResolver.Resolve(tree.Value, overrides);

        return Ok(resolution.Roles);
    }

    /// <summary>
    /// Assigns a role to a folder. Validated against the live mailbox, never against the
    /// client's tree: the folder must exist, be selectable, and not be the inbox. The
    /// identity guard (uid_validity, mailbox_id) is captured server-side from the live
    /// folder — the client only names the role and the path.
    /// </summary>
    /// <param name="request">role and folder path</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Override stored</response>
    /// <response code="400">Unknown role, missing path, inbox target, non-selectable folder, or folder already holding another role</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">The folder no longer exists</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpPut("FolderRoles")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> SetFolderRole(SetFolderRoleRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");
        if (!FolderRoles.IsValid(request.Role)) return BadRequestEnveloppe("Unknown folder role");
        if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequestEnveloppe("A folder path is required");
        if (string.Equals(request.FolderPath, "INBOX", StringComparison.OrdinalIgnoreCase))
            return BadRequestEnveloppe("The inbox cannot be assigned a role");

        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        var status = await folders.GetFolderStatusAsync(AuthenticatedUser, password, request.FolderPath, cancellationToken);
        if (status.IsFailure)
        {
            return status.Error == ImapSession.FolderNotFound
                ? NotFoundEnveloppe(status.Error)
                : BadGatewayEnveloppe(status.Error);
        }

        if (!status.Value.Selectable)
            return BadRequestEnveloppe("This folder cannot hold messages");

        var userId = AuthenticatedUser.WebmailUid;
        var overrides = await roleStore.GetAsync(userId, cancellationToken);

        // Guard against the resolver's output, not the raw rows. A stored row whose folder
        // no longer resolves holds nothing — the resolver reports it stale and the Settings
        // picker offers that folder again — so a raw-row check rejected exactly the folder
        // the UI had just offered. The two must read the same data the same way.
        var tree = await folders.GetTreeAsync(AuthenticatedUser, password, cancellationToken);
        if (tree.IsFailure)
            return BadGatewayEnveloppe(tree.Error);

        var holder = FolderRoleResolver.Resolve(tree.Value, overrides).Roles.FirstOrDefault(
            e => e.Provenance == "override" && e.FolderPath == request.FolderPath && e.Role != request.Role);
        if (holder != null)
            return BadRequestEnveloppe(
                $"This folder is already assigned to {holder.Role}. Set {holder.Role} back to automatic, or point it at another folder, first.");

        await roleStore.UpsertAsync(new FolderRoleOverride
        {
            UserId = userId,
            Role = request.Role!,
            FolderPath = request.FolderPath,
            UidValidity = status.Value.UidValidity,
            MailboxId = status.Value.MailboxId
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>Clears an override; the role goes back to discovery. Idempotent.</summary>
    /// <param name="role">role to clear</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Override cleared, or was already absent</response>
    /// <response code="400">Unknown role</response>
    /// <response code="401">Not authenticated</response>
    [HttpDelete("FolderRoles")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> ClearFolderRole([FromQuery] string? role, CancellationToken cancellationToken)
    {
        if (!FolderRoles.IsValid(role)) return BadRequestEnveloppe("Unknown folder role");

        await roleStore.DeleteAsync(AuthenticatedUser.WebmailUid, role!, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// One page of a folder, newest message first. The folder path travels in the query
    /// string rather than a route segment because the hierarchy separator may be '/'.
    /// </summary>
    /// <param name="folder">full folder path</param>
    /// <param name="page">zero-based page index</param>
    /// <param name="pageSize">messages per page, 1 to 200</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The page, with the folder's UidValidity</response>
    /// <response code="400">The folder is missing, or the paging arguments are out of range</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpGet("Messages")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<MailFolderPage>> GetMessages(
        [FromQuery] string folder,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folder)) return BadRequestEnveloppe("A folder is required");
        if (page < 0) return BadRequestEnveloppe("Page must not be negative");

        // An unbounded page size lets one request pull an entire mailbox.
        if (pageSize is < 1 or > 200) return BadRequestEnveloppe("Page size must be between 1 and 200");

        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        var result = await messages.ListAsync(AuthenticatedUser, password, folder, page, pageSize, cancellationToken);

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
    /// <response code="502">The mail server could not be reached</response>
    [HttpGet("Messages/Detail")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<MailMessageDetail>> GetMessage(
        [FromQuery] string folder,
        [FromQuery] uint uid,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folder)) return BadRequestEnveloppe("A folder is required");

        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        var result = await messages.GetAsync(AuthenticatedUser, password, folder, uid, cancellationToken);

        if (result.IsFailure && result.Error == ImapSession.MessageNotFound)
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
    /// <response code="502">The mail server could not be reached</response>
    [HttpGet("Messages/Attachment")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> GetAttachment(
        [FromQuery] string folder,
        [FromQuery] uint uid,
        [FromQuery] string? part,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folder)) return BadRequestEnveloppe("A folder is required");

        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        var result = await messages.GetAttachmentAsync(
            AuthenticatedUser, password, folder, uid, part ?? string.Empty, cancellationToken);

        if (result.IsFailure)
        {
            var status = result.Error is ImapSession.MessageNotFound or ImapSession.AttachmentNotFound
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status502BadGateway;

            return StatusCode(status, ResultEnveloppe.CreateErrorEnveloppe(result.Error));
        }

        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
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
    /// <response code="502">The mail server could not be reached</response>
    [HttpPut("Messages/Flags")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> SetMessageFlags(SetMessageFlagsRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequestEnveloppe("A folder is required");
        if (request.Uids.Count is < 1 or > 200) return BadRequestEnveloppe("Uids must hold between 1 and 200 entries");

        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        var result = await messages.SetFlagsAsync(
            AuthenticatedUser, password, request.FolderPath, request.Uids, request.Flag, request.Value, cancellationToken);

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway, successStatusCode: StatusCodes.Status204NoContent);
    }

    /// <summary>Moves a batch of messages into another folder.</summary>
    /// <param name="request">source folder, UIDs and target folder</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">The messages were moved</response>
    /// <response code="400">A folder is missing, the batch is empty or above 200 UIDs, the target equals the source, or the target cannot hold messages</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpPost("Messages/Move")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> MoveMessages(MoveMessagesRequest request, CancellationToken cancellationToken)
        => MoveOrCopy(request, copy: false, cancellationToken);

    /// <summary>Copies a batch of messages into another folder.</summary>
    /// <param name="request">source folder, UIDs and target folder</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">The messages were copied</response>
    /// <response code="400">A folder is missing, the batch is empty or above 200 UIDs, the target equals the source, or the target cannot hold messages</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpPost("Messages/Copy")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
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

        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        var result = await messages.MoveOrCopyAsync(
            AuthenticatedUser, password, request.FolderPath, request.Uids, request.TargetFolderPath, copy, cancellationToken);

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
    /// <response code="502">The mail server could not be reached, or cannot delete without UIDPLUS</response>
    [HttpDelete("Messages")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> DeleteMessages(DeleteMessagesRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequestEnveloppe("A folder is required");
        if (request.Uids.Count is < 1 or > 200) return BadRequestEnveloppe("Uids must hold between 1 and 200 entries");

        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        var result = await messages.DeleteAsync(AuthenticatedUser, password, request.FolderPath, request.Uids, cancellationToken);

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway, successStatusCode: StatusCodes.Status204NoContent);
    }

    /// <summary>Empties a whole folder: purge (no target) or move every message to a target.</summary>
    /// <param name="request">source folder and optional target (blank = purge)</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">The folder was emptied</response>
    /// <response code="400">The source is missing, the target equals the source, or the target cannot hold messages</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpPost("Folders/Empty")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> EmptyFolder(EmptyFolderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FolderPath))
            return BadRequestEnveloppe("A folder is required");
        if (!string.IsNullOrWhiteSpace(request.TargetFolderPath)
            && string.Equals(request.FolderPath, request.TargetFolderPath, StringComparison.Ordinal))
            return BadRequestEnveloppe("The target folder must differ from the source folder");

        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        var result = await messages.EmptyAsync(
            AuthenticatedUser, password, request.FolderPath, request.TargetFolderPath, cancellationToken);

        if (result.IsFailure && result.Error == ImapSession.TargetNotSelectable)
            return BadRequestEnveloppe("The target folder cannot hold messages");

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
    /// <response code="502">The mail server could not be reached</response>
    [HttpPost("Messages/Search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<MailSearchPage>> SearchMessages(SearchMessagesRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");
        if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequestEnveloppe("A folder is required");
        if (request.Page < 0) return BadRequestEnveloppe("Page must not be negative");
        if (request.PageSize is < 1 or > 200) return BadRequestEnveloppe("Page size must be between 1 and 200");

        var criteria = new MailSearchCriteria(
            request.Quick, request.From, request.To, request.Subject, request.Text,
            request.SinceDays, request.Unread, request.Flagged, request.HasAttachment);
        if (!MailSearchQueryBuilder.HasAnyCriterion(criteria))
            return BadRequestEnveloppe("At least one search criterion is required");

        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        var result = await messages.SearchAsync(
            AuthenticatedUser, password, request.FolderPath, request.AllFolders,
            criteria, request.Page, request.PageSize, cancellationToken);

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>
    /// Stages one outgoing attachment. Files upload as they are added — the Gmail/Rainloop
    /// model — and Send references the returned ids. No IMAP involved, so no credentials
    /// cookie is read. The body is capped at the configured message size before model binding
    /// (<see cref="AttachmentSizeLimitFilter"/>), and the store re-checks it while streaming.
    /// </summary>
    /// <param name="file">the uploaded file</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Id and metadata of the staged file</response>
    /// <response code="400">No file, file over the limit, or account staging cap reached</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="413">The request body is over the configured message size</response>
    [HttpPost("Attachments")]
    [ServiceFilter(typeof(AttachmentSizeLimitFilter))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<StagedAttachmentInfo>> UploadAttachment(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            return BadRequestEnveloppe("A file is required");

        await using var content = file.OpenReadStream();
        var result = await staged.SaveAsync(
            AuthenticatedUser.WebmailUid.ToString(), file.FileName, file.ContentType, content, cancellationToken);

        return FromResult(result);
    }

    /// <summary>
    /// Removes one staged attachment. Always 204: the namespace is sealed per account, so an
    /// unknown or foreign id resolves to nothing — and deleting nothing is idempotent success.
    /// </summary>
    /// <param name="id">staged attachment id</param>
    /// <response code="204">Gone, or never was</response>
    /// <response code="401">Not authenticated</response>
    [HttpDelete("Attachments/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult DeleteAttachment(Guid id)
    {
        staged.Delete(AuthenticatedUser.WebmailUid.ToString(), id);
        return NoContent();
    }

    /// <summary>
    /// Serves one staged attachment back to its owner, so the composer can display the inline
    /// images PrepareQuote staged. Always an attachment disposition plus nosniff: an img
    /// subresource renders regardless, while navigating to the URL downloads instead of
    /// rendering staged mail content on our origin.
    /// </summary>
    /// <param name="id">staged attachment id</param>
    /// <response code="200">The staged bytes</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">Unknown id, or one staged by another account</response>
    [HttpGet("Attachments/{id:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetStagedAttachment(Guid id)
    {
        var result = staged.Open(AuthenticatedUser.WebmailUid.ToString(), id);
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

    /// <summary>
    /// Sends a composed message: sanitised multipart/alternative body, staged attachments,
    /// then a \Seen copy APPENDed to the sent role. The SMTP envelope is derived from the
    /// recipients and MailKit strips the Bcc header at transmission, so only the addressees
    /// see it went out; the filed Sent copy keeps the header so the sender can see who was
    /// blind-copied. A failed copy never fails the send — the response says which happened.
    /// An optional fromAddress sends as one of the account's own addresses (primary or a live
    /// alias); the display label is resolved server-side, never taken from the request.
    /// </summary>
    /// <param name="request">recipients, subject, HTML body, staged attachment ids and an optional fromAddress</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Sent; appendedToSent tells whether the copy was filed</response>
    /// <response code="400">No recipient, an invalid address, a fromAddress the account does not own, or a staged id no longer available</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="502">The mail server refused the submission</response>
    [HttpPost("Send")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<SendMessageResult>> SendMessage(SendMessageRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");

        var invalid = NormalizeOutgoing(request, requireRecipient: true, out var normalized);
        if (invalid != null) return invalid;
        request = normalized;

        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        var result = await sender.SendAsync(AuthenticatedUser, password, request, cancellationToken);

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
    /// <response code="502">The mail server could not be reached</response>
    [HttpPost("Messages/PrepareQuote")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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

        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        var message = await messages.GetMimeMessageAsync(
            AuthenticatedUser, password, request.Folder, request.Uid, cancellationToken);
        if (message.IsFailure && message.Error == ImapSession.MessageNotFound)
            return NotFoundEnveloppe(message.Error);
        if (message.IsFailure)
            return BadGatewayEnveloppe(message.Error);

        var prepared = await quotes.PrepareAsync(
            AuthenticatedUser.WebmailUid.ToString(), message.Value, purpose.Value, cancellationToken);

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
    /// <response code="502">No folder holds the drafts role, or the mail server refused the save</response>
    [HttpPost("Drafts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<SavedDraft>> SaveDraft(SaveDraftRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");

        // No recipient gate here: an empty or recipient-less draft is valid, unlike a send.
        var invalid = NormalizeOutgoing(request, requireRecipient: false, out var normalized);
        if (invalid != null) return invalid;
        request = (SaveDraftRequest)normalized;

        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        var result = await drafts.SaveAsync(AuthenticatedUser, password, request, cancellationToken);

        if (result.IsFailure)
        {
            var refused = RefusedBuild(result.Error, request.FromAddress);
            if (refused != null) return refused;
            if (result.Error == IDraftSaver.NoDraftsFolder)
                return BadGatewayEnveloppe(
                    "This mailbox has no drafts folder. Assign the drafts role in Settings > Folders list.");
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
    /// <response code="502">The mail server could not be reached</response>
    [HttpPost("Drafts/Open")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<OpenedDraft>> OpenDraft(OpenDraftRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Folder))
            return BadRequestEnveloppe("A folder is required");

        if (!TryMailPassword(out var password, out var unauthorized)) return unauthorized;

        var message = await messages.GetMimeMessageAsync(
            AuthenticatedUser, password, request.Folder, request.Uid, cancellationToken);
        if (message.IsFailure && message.Error == ImapSession.MessageNotFound)
            return NotFoundEnveloppe(message.Error);
        if (message.IsFailure)
            return BadGatewayEnveloppe(message.Error);

        var prepared = await quotes.PrepareAsync(
            AuthenticatedUser.WebmailUid.ToString(), message.Value, QuotePurpose.EditAsNew, cancellationToken);

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
            message.References?.ToList() ?? []);

    private static List<string> Addresses(InternetAddressList? list) =>
        list?.Mailboxes?.Select(m => m.Address).ToList() ?? [];

    private static void StampRoles(IReadOnlyList<MailFolderNode> nodes, IReadOnlyDictionary<string, string> roleByPath)
    {
        foreach (var node in nodes)
        {
            node.SpecialUse = roleByPath.TryGetValue(node.Path, out var role) ? role : null;
            StampRoles(node.Children, roleByPath);
        }
    }
}
