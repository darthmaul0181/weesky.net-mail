using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
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
public sealed class MailController : ApiBaseController
{
    private readonly IMailFolderRepository _folders;
    private readonly IMailMessageRepository _messages;
    private readonly IMailCredentialStore _credentials;
    private readonly IFolderRoleStore _roleStore;
    private readonly IStagedAttachmentStore _staged;
    private readonly IMailSender _sender;
    private readonly IQuotePreparer _quotes;
    private readonly IDraftSaver _drafts;

    public MailController(
        IMailFolderRepository folders,
        IMailMessageRepository messages,
        IMailCredentialStore credentials,
        IFolderRoleStore roleStore,
        IStagedAttachmentStore staged,
        IMailSender sender,
        IQuotePreparer quotes,
        IDraftSaver drafts)
    {
        _folders = folders;
        _messages = messages;
        _credentials = credentials;
        _roleStore = roleStore;
        _staged = staged;
        _sender = sender;
        _quotes = quotes;
        _drafts = drafts;
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
        var tree = await _folders.GetTreeAsync(AuthenticatedUser, password, cancellationToken);
        if (tree.IsFailure)
            return StatusCode(StatusCodes.Status502BadGateway, ResultEnveloppe.CreateErrorEnveloppe(tree.Error));

        var overrides = await _roleStore.GetAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        var roleByPath = FolderRoleResolver.Resolve(tree.Value, overrides).RoleByPath;

        if (roleByPath.TryGetValue(path, out var role))
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(
                $"This folder is the {role} folder and cannot be {verb}. Point {role} at another folder first."));

        if (includeDescendants && tree.Value.FindByPath(path) is { } target)
        {
            foreach (var descendant in target.Descendants())
            {
                if (roleByPath.TryGetValue(descendant.Path, out var childRole))
                    return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(
                        $"\"{descendant.Name}\" inside this folder is the {childRole} folder, so deleting it would take {childRole} with it. Point {childRole} at another folder first."));
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
        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var result = await _folders.GetTreeAsync(AuthenticatedUser, password.Value, cancellationToken);
        if (result.IsFailure)
            return StatusCode(StatusCodes.Status502BadGateway, ResultEnveloppe.CreateErrorEnveloppe(result.Error));

        // The tree's SpecialUse is the resolution chain's output, not raw discovery: a
        // user override reassigns the role, and the displaced folder shows under its own
        // name (spec § 4.1).
        var overrides = await _roleStore.GetAsync(AuthenticatedUser.WebmailUid, cancellationToken);
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
        if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder name is required"));

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var result = await _folders.CreateFolderAsync(
            AuthenticatedUser, password.Value, request.ParentPath ?? string.Empty, request.Name, cancellationToken);

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
        if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));
        if (string.IsNullOrWhiteSpace(request.Path)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder path is required"));
        if (string.IsNullOrWhiteSpace(request.NewName)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder name is required"));

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        if (await RefuseIfSystemFolderAsync(
                password.Value, request.Path, "renamed", includeDescendants: false, cancellationToken) is { } refusal)
            return refusal;

        var result = await _folders.RenameFolderAsync(
            AuthenticatedUser, password.Value, request.Path, request.NewParentPath ?? string.Empty, request.NewName, cancellationToken);

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
        if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));
        if (string.IsNullOrWhiteSpace(request.Path)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder path is required"));

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        if (await RefuseIfSystemFolderAsync(
                password.Value, request.Path, "deleted", includeDescendants: true, cancellationToken) is { } refusal)
            return refusal;

        var result = await _folders.DeleteFolderAsync(AuthenticatedUser, password.Value, request.Path, cancellationToken);

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
        if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));
        if (string.IsNullOrWhiteSpace(request.Path)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder path is required"));

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        // Only hiding is refused: refusing to subscribe would leave a mailbox whose trash
        // another client hid stuck that way.
        if (!request.Subscribed && await RefuseIfSystemFolderAsync(
                password.Value, request.Path, "hidden", includeDescendants: false, cancellationToken) is { } refusal)
            return refusal;

        var result = await _folders.SetSubscriptionAsync(
            AuthenticatedUser, password.Value, request.Path, request.Subscribed, cancellationToken);

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
        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var tree = await _folders.GetTreeAsync(AuthenticatedUser, password.Value, cancellationToken);
        if (tree.IsFailure)
            return StatusCode(StatusCodes.Status502BadGateway, ResultEnveloppe.CreateErrorEnveloppe(tree.Error));

        var overrides = await _roleStore.GetAsync(AuthenticatedUser.WebmailUid, cancellationToken);
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
        if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));
        if (!FolderRoles.IsValid(request.Role)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Unknown folder role"));
        if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder path is required"));
        if (string.Equals(request.FolderPath, "INBOX", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("The inbox cannot be assigned a role"));

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var status = await _folders.GetFolderStatusAsync(AuthenticatedUser, password.Value, request.FolderPath, cancellationToken);
        if (status.IsFailure)
        {
            return status.Error == ImapSession.FolderNotFound
                ? NotFound(ResultEnveloppe.CreateErrorEnveloppe(status.Error))
                : StatusCode(StatusCodes.Status502BadGateway, ResultEnveloppe.CreateErrorEnveloppe(status.Error));
        }

        if (!status.Value.Selectable)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("This folder cannot hold messages"));

        var userId = AuthenticatedUser.WebmailUid;
        var overrides = await _roleStore.GetAsync(userId, cancellationToken);

        // Guard against the resolver's output, not the raw rows. A stored row whose folder
        // no longer resolves holds nothing — the resolver reports it stale and the Settings
        // picker offers that folder again — so a raw-row check rejected exactly the folder
        // the UI had just offered. The two must read the same data the same way.
        var tree = await _folders.GetTreeAsync(AuthenticatedUser, password.Value, cancellationToken);
        if (tree.IsFailure)
            return StatusCode(StatusCodes.Status502BadGateway, ResultEnveloppe.CreateErrorEnveloppe(tree.Error));

        var holder = FolderRoleResolver.Resolve(tree.Value, overrides).Roles.FirstOrDefault(
            e => e.Provenance == "override" && e.FolderPath == request.FolderPath && e.Role != request.Role);
        if (holder != null)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(
                $"This folder is already assigned to {holder.Role}. Set {holder.Role} back to automatic, or point it at another folder, first."));

        await _roleStore.UpsertAsync(new FolderRoleOverride
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
        if (!FolderRoles.IsValid(role)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Unknown folder role"));

        await _roleStore.DeleteAsync(AuthenticatedUser.WebmailUid, role!, cancellationToken);
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
        if (string.IsNullOrWhiteSpace(folder)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));
        if (page < 0) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Page must not be negative"));

        // An unbounded page size lets one request pull an entire mailbox.
        if (pageSize is < 1 or > 200) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Page size must be between 1 and 200"));

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var result = await _messages.ListAsync(AuthenticatedUser, password.Value, folder, page, pageSize, cancellationToken);

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
        if (string.IsNullOrWhiteSpace(folder)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var result = await _messages.GetAsync(AuthenticatedUser, password.Value, folder, uid, cancellationToken);

        if (result.IsFailure && result.Error == ImapSession.MessageNotFound)
        {
            return NotFound(ResultEnveloppe.CreateErrorEnveloppe(result.Error));
        }

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>
    /// Downloads one attachment. Always served as an attachment disposition: message
    /// content must never render inline in the browser.
    /// </summary>
    /// <param name="folder">full folder path</param>
    /// <param name="uid">message UID</param>
    /// <param name="part">MIME part specifier, taken from the message's attachment list</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The attachment bytes</response>
    /// <response code="400">The folder or the part is missing</response>
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
        [FromQuery] string part,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folder)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));
        if (string.IsNullOrWhiteSpace(part)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A part is required"));

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var result = await _messages.GetAttachmentAsync(AuthenticatedUser, password.Value, folder, uid, part, cancellationToken);

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
        if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));
        if (request.Uids.Count is < 1 or > 200) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Uids must hold between 1 and 200 entries"));

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var result = await _messages.SetFlagsAsync(
            AuthenticatedUser, password.Value, request.FolderPath, request.Uids, request.Flag, request.Value, cancellationToken);

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
        if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));
        if (request.Uids.Count is < 1 or > 200) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Uids must hold between 1 and 200 entries"));
        if (string.IsNullOrWhiteSpace(request.TargetFolderPath)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A target folder is required"));
        if (string.Equals(request.FolderPath, request.TargetFolderPath, StringComparison.Ordinal))
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("The target folder must differ from the source folder"));

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var result = await _messages.MoveOrCopyAsync(
            AuthenticatedUser, password.Value, request.FolderPath, request.Uids, request.TargetFolderPath, copy, cancellationToken);

        if (result.IsFailure && result.Error == ImapSession.TargetNotSelectable)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("The target folder cannot hold messages"));

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
        if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));
        if (request.Uids.Count is < 1 or > 200) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Uids must hold between 1 and 200 entries"));

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var result = await _messages.DeleteAsync(AuthenticatedUser, password.Value, request.FolderPath, request.Uids, cancellationToken);

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
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));
        if (!string.IsNullOrWhiteSpace(request.TargetFolderPath)
            && string.Equals(request.FolderPath, request.TargetFolderPath, StringComparison.Ordinal))
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("The target folder must differ from the source folder"));

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var result = await _messages.EmptyAsync(
            AuthenticatedUser, password.Value, request.FolderPath, request.TargetFolderPath, cancellationToken);

        if (result.IsFailure && result.Error == ImapSession.TargetNotSelectable)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("The target folder cannot hold messages"));

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
        if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));
        if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));
        if (request.Page < 0) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Page must not be negative"));
        if (request.PageSize is < 1 or > 200) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Page size must be between 1 and 200"));

        var criteria = new MailSearchCriteria(
            request.Quick, request.From, request.To, request.Subject, request.Text,
            request.SinceDays, request.Unread, request.Flagged, request.HasAttachment);
        if (!MailSearchQueryBuilder.HasAnyCriterion(criteria))
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("At least one search criterion is required"));

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var result = await _messages.SearchAsync(
            AuthenticatedUser, password.Value, request.FolderPath, request.AllFolders,
            criteria, request.Page, request.PageSize, cancellationToken);

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>
    /// Stages one outgoing attachment. Files upload as they are added — the Gmail/Rainloop
    /// model — and Send references the returned ids. No IMAP involved, so no credentials
    /// cookie is read. Kestrel's body cap is disabled: the store enforces the configured
    /// limit itself while streaming.
    /// </summary>
    /// <param name="file">the uploaded file</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Id and metadata of the staged file</response>
    /// <response code="400">No file, file over the limit, or account staging cap reached</response>
    /// <response code="401">Not authenticated</response>
    [HttpPost("Attachments")]
    [DisableRequestSizeLimit]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<StagedAttachmentInfo>> UploadAttachment(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A file is required"));

        await using var content = file.OpenReadStream();
        var result = await _staged.SaveAsync(
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
        _staged.Delete(AuthenticatedUser.WebmailUid.ToString(), id);
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
        var result = _staged.Open(AuthenticatedUser.WebmailUid.ToString(), id);
        if (result.IsFailure) return NotFound(ResultEnveloppe.CreateErrorEnveloppe("Attachment not found"));

        Response.Headers.XContentTypeOptions = "nosniff";
        try
        {
            var stream = System.IO.File.OpenRead(result.Value.FilePath);
            return File(stream, result.Value.Info.ContentType, result.Value.Info.FileName);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Vanished between Open and read (TTL sweep / concurrent DELETE).
            return NotFound(ResultEnveloppe.CreateErrorEnveloppe("Attachment not found"));
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
        if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));

        // An explicit "to": null in the body overrides the record's [] default; normalise every
        // recipient list so a null never NREs downstream.
        request = request with { To = request.To ?? [], Cc = request.Cc ?? [], Bcc = request.Bcc ?? [], References = request.References ?? [] };
        if (request.To.Count == 0) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("At least one recipient is required"));

        foreach (var address in request.To.Concat(request.Cc).Concat(request.Bcc))
        {
            if (string.IsNullOrWhiteSpace(address) || !MailboxAddress.TryParse(RecipientAddressParser.Options, address, out _))
                return BadRequest(ResultEnveloppe.CreateErrorEnveloppe($"\"{address}\" is not a valid email address"));
        }

        if (!string.IsNullOrWhiteSpace(request.FromAddress))
        {
            // Parse once and keep the bare address: a decorated "Name <a@b.c>" would never match an
            // alias downstream and would be refused as foreign instead of accepted.
            if (!MailboxAddress.TryParse(RecipientAddressParser.Options, request.FromAddress, out var from))
                return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(
                    $"\"{request.FromAddress}\" is not a valid email address"));
            request = request with { FromAddress = from.Address };
        }

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var result = await _sender.SendAsync(AuthenticatedUser, password.Value, request, cancellationToken);

        if (result.IsFailure && result.Error == IMailSender.UnknownAttachment)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(
                "An attachment is no longer available; remove it and attach it again"));

        if (result.IsFailure && result.Error == IMailSender.ForbiddenFrom)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(
                $"Sending from \"{request.FromAddress}\" is not allowed on this account"));

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
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));

        QuotePurpose? purpose = request.Purpose switch
        {
            "reply" => QuotePurpose.Reply,
            "forward" => QuotePurpose.Forward,
            "editAsNew" => QuotePurpose.EditAsNew,
            _ => null,
        };
        if (purpose == null)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Purpose must be reply, forward or editAsNew"));

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var message = await _messages.GetMimeMessageAsync(
            AuthenticatedUser, password.Value, request.Folder, request.Uid, cancellationToken);
        if (message.IsFailure && message.Error == ImapSession.MessageNotFound)
            return NotFound(ResultEnveloppe.CreateErrorEnveloppe(message.Error));
        if (message.IsFailure)
            return StatusCode(StatusCodes.Status502BadGateway, ResultEnveloppe.CreateErrorEnveloppe(message.Error));

        var prepared = await _quotes.PrepareAsync(
            AuthenticatedUser.WebmailUid.ToString(), message.Value, purpose.Value, cancellationToken);

        // A failure here is the staging caps talking (file size / account quota): 400, actionable.
        if (prepared.IsFailure)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(prepared.Error));

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
        if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));

        request = request with { To = request.To ?? [], Cc = request.Cc ?? [], Bcc = request.Bcc ?? [], References = request.References ?? [] };

        foreach (var address in request.To.Concat(request.Cc).Concat(request.Bcc))
        {
            if (string.IsNullOrWhiteSpace(address) || !MailboxAddress.TryParse(RecipientAddressParser.Options, address, out _))
                return BadRequest(ResultEnveloppe.CreateErrorEnveloppe($"\"{address}\" is not a valid email address"));
        }

        if (!string.IsNullOrWhiteSpace(request.FromAddress))
        {
            if (!MailboxAddress.TryParse(RecipientAddressParser.Options, request.FromAddress, out var from))
                return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(
                    $"\"{request.FromAddress}\" is not a valid email address"));
            request = request with { FromAddress = from.Address };
        }

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var result = await _drafts.SaveAsync(AuthenticatedUser, password.Value, request, cancellationToken);

        if (result.IsFailure && result.Error == IOutgoingMessageFactory.UnknownAttachment)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(
                "An attachment is no longer available; remove it and attach it again"));
        if (result.IsFailure && result.Error == IOutgoingMessageFactory.ForbiddenFrom)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(
                $"Sending from \"{request.FromAddress}\" is not allowed on this account"));
        if (result.IsFailure && result.Error == IDraftSaver.NoDraftsFolder)
            return StatusCode(StatusCodes.Status502BadGateway, ResultEnveloppe.CreateErrorEnveloppe(
                "This mailbox has no drafts folder. Assign the drafts role in Settings > Folders list."));

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
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
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var message = await _messages.GetMimeMessageAsync(
            AuthenticatedUser, password.Value, request.Folder, request.Uid, cancellationToken);
        if (message.IsFailure && message.Error == ImapSession.MessageNotFound)
            return NotFound(ResultEnveloppe.CreateErrorEnveloppe(message.Error));
        if (message.IsFailure)
            return StatusCode(StatusCodes.Status502BadGateway, ResultEnveloppe.CreateErrorEnveloppe(message.Error));

        var prepared = await _quotes.PrepareAsync(
            AuthenticatedUser.WebmailUid.ToString(), message.Value, QuotePurpose.EditAsNew, cancellationToken);

        // A failure here is the staging caps talking (file size / account quota): 400, actionable.
        if (prepared.IsFailure)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(prepared.Error));

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
