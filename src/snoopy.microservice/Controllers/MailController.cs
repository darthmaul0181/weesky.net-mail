using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using MimeKit.Utils;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// Mail access over IMAP, on the account the request names.
///
/// Three conventions hold across every action. Folder paths never appear in a route
/// segment — the hierarchy separator may be '/', which would break routing — so they
/// travel in the query string or the request body. Every action resolves the active
/// account first, so nothing ever serves the primary mailbox while the user is looking at
/// a connected one. And the failure modes are distinct: a missing or undecryptable
/// credentials cookie is 401 ("credentials_unavailable") so the client can sign in again,
/// an unknown or foreign account is 404, a connected account whose stored secret no longer
/// decrypts is 409, and anything the mail server refuses is 502.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class MailController(
    IMailFolderRepository folders,
    IMailMessageRepository messages,
    IAccountConnectionResolver connections,
    IFolderRoleStore roleStore,
    IStagedAttachmentStore staged,
    IMailSender sender,
    IQuotePreparer quotes,
    IDraftSaver drafts,
    ITrustedSenderStore trustedSenders,
    ILogger<MailController> logger) : ApiBaseController
{
    /// <summary>
    /// How much of a message the source view may carry. Internal, never a setting: headers sit
    /// at the head of the file, so what a cap drops is the tail of the base64 — and a message
    /// may legitimately weigh MailOptions.MaxMessageSizeMb (25 MB), which no browser wants
    /// dropped into a &lt;pre&gt;.
    /// </summary>
    private const int MaxSourceBytes = 1024 * 1024;

    /// <summary>
    /// The active account's connection — endpoints plus credentials — or the error to answer
    /// with. Every mail action reads a mailbox as the user, so every one of them starts here.
    /// A guard rather than an action filter on purpose: the controller tests invoke actions
    /// directly and no filter would run, so moving the check into the pipeline would move it
    /// out of the tests that cover it.
    /// </summary>
    private async Task<(MailAccountConnection? Connection, ActionResult? Error)> TryResolveAsync(
        CancellationToken cancellationToken)
    {
        var resolved = await connections.ResolveAsync(AuthenticatedUser, Request, cancellationToken);
        if (resolved.IsSuccess) return (resolved.Value, null);

        return (null, resolved.Error switch
        {
            ConnectedAccountErrors.AccountNotFound => NotFoundEnveloppe(resolved.Error),
            ConnectedAccountErrors.CredentialsInvalid => ConflictEnveloppe(resolved.Error),
            _ => UnauthorizedEnveloppe(resolved.Error),
        });
    }

    /// <summary>
    /// Refuses an operation on a folder holding a well-known role, and returns null when it
    /// may proceed. Renaming or deleting one breaks the role for every client on the mailbox;
    /// hiding one strands whatever gets filed into it.
    /// </summary>
    /// <param name="connection">the active account's connection</param>
    /// <param name="path">folder the operation targets</param>
    /// <param name="verb">what the caller is trying to do, for the message</param>
    /// <param name="includeDescendants">
    /// True for deletion: removing a parent takes its children with it, so a guard on the
    /// target path alone could be stepped around one level up.
    /// </param>
    /// <param name="cancellationToken">cancellation token</param>
    private async Task<ActionResult?> RefuseIfSystemFolderAsync(
        MailAccountConnection connection, string path, string verb, bool includeDescendants, CancellationToken cancellationToken)
    {
        var tree = await folders.GetTreeAsync(AuthenticatedUser, connection, cancellationToken);
        if (tree.IsFailure)
            return BadGatewayEnveloppe(tree.Error);

        var overrides = await roleStore.GetAsync(AuthenticatedUser.WebmailUid, connection.StorageAccountId, cancellationToken);
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
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpGet("Folders")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<MailFolderNode>>> GetFolders(CancellationToken cancellationToken)
    {
        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

        var result = await folders.GetTreeAsync(AuthenticatedUser, connection, cancellationToken);
        if (result.IsFailure)
            return BadGatewayEnveloppe(result.Error);

        // The tree's SpecialUse is the resolution chain's output, not raw discovery: a
        // user override reassigns the role, and the displaced folder shows under its own
        // name (spec § 4.1).
        var overrides = await roleStore.GetAsync(AuthenticatedUser.WebmailUid, connection.StorageAccountId, cancellationToken);
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
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server refused the operation</response>
    [HttpPost("Folders")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<string>> CreateFolder(CreateFolderRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequestEnveloppe("A folder name is required");

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

        var result = await folders.CreateFolderAsync(
            AuthenticatedUser, connection, request.ParentPath ?? string.Empty, request.Name, cancellationToken);

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>Renames a folder, optionally moving it under a different parent.</summary>
    /// <param name="request">current path, new parent path and new leaf name</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Path of the renamed folder</response>
    /// <response code="400">The request body, the path or the new name is missing</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server refused the operation</response>
    [HttpPut("Folders")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<string>> RenameFolder(RenameFolderRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");
        if (string.IsNullOrWhiteSpace(request.Path)) return BadRequestEnveloppe("A folder path is required");
        if (string.IsNullOrWhiteSpace(request.NewName)) return BadRequestEnveloppe("A folder name is required");

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

        if (await RefuseIfSystemFolderAsync(
                connection, request.Path, "renamed", includeDescendants: false, cancellationToken) is { } refusal)
            return refusal;

        var result = await folders.RenameFolderAsync(
            AuthenticatedUser, connection, request.Path, request.NewParentPath ?? string.Empty, request.NewName, cancellationToken);

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>Deletes a folder. The inbox cannot be deleted.</summary>
    /// <param name="request">path of the folder to delete</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Folder deleted</response>
    /// <response code="400">The request body or the path is missing</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server refused the operation</response>
    [HttpDelete("Folders")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> DeleteFolder(DeleteFolderRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");
        if (string.IsNullOrWhiteSpace(request.Path)) return BadRequestEnveloppe("A folder path is required");

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

        if (await RefuseIfSystemFolderAsync(
                connection, request.Path, "deleted", includeDescendants: true, cancellationToken) is { } refusal)
            return refusal;

        var result = await folders.DeleteFolderAsync(AuthenticatedUser, connection, request.Path, cancellationToken);

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
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server refused the operation</response>
    [HttpPut("Folders/Subscription")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> SetFolderSubscription(FolderSubscriptionRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");
        if (string.IsNullOrWhiteSpace(request.Path)) return BadRequestEnveloppe("A folder path is required");

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

        // Only hiding is refused: refusing to subscribe would leave a mailbox whose trash
        // another client hid stuck that way.
        if (!request.Subscribed && await RefuseIfSystemFolderAsync(
                connection, request.Path, "hidden", includeDescendants: false, cancellationToken) is { } refusal)
            return refusal;

        var result = await folders.SetSubscriptionAsync(
            AuthenticatedUser, connection, request.Path, request.Subscribed, cancellationToken);

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
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpGet("FolderRoles")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<FolderRoleEntry>>> GetFolderRoles(CancellationToken cancellationToken)
    {
        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

        var tree = await folders.GetTreeAsync(AuthenticatedUser, connection, cancellationToken);
        if (tree.IsFailure)
            return BadGatewayEnveloppe(tree.Error);

        var overrides = await roleStore.GetAsync(AuthenticatedUser.WebmailUid, connection.StorageAccountId, cancellationToken);
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
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpPut("FolderRoles")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> SetFolderRole(SetFolderRoleRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");
        if (!FolderRoles.IsValid(request.Role)) return BadRequestEnveloppe("Unknown folder role");
        if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequestEnveloppe("A folder path is required");
        if (string.Equals(request.FolderPath, "INBOX", StringComparison.OrdinalIgnoreCase))
            return BadRequestEnveloppe("The inbox cannot be assigned a role");

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

        var status = await folders.GetFolderStatusAsync(AuthenticatedUser, connection, request.FolderPath, cancellationToken);
        if (status.IsFailure)
        {
            return IsMissing(status.Error)
                ? NotFoundEnveloppe(status.Error)
                : BadGatewayEnveloppe(status.Error);
        }

        if (!status.Value.Selectable)
            return BadRequestEnveloppe("This folder cannot hold messages");

        var userId = AuthenticatedUser.WebmailUid;
        var overrides = await roleStore.GetAsync(userId, connection.StorageAccountId, cancellationToken);

        // Guard against the resolver's output, not the raw rows. A stored row whose folder
        // no longer resolves holds nothing — the resolver reports it stale and the Settings
        // picker offers that folder again — so a raw-row check rejected exactly the folder
        // the UI had just offered. The two must read the same data the same way.
        var tree = await folders.GetTreeAsync(AuthenticatedUser, connection, cancellationToken);
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
            AccountId = connection.StorageAccountId,
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
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    [HttpDelete("FolderRoles")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> ClearFolderRole([FromQuery] string? role, CancellationToken cancellationToken)
    {
        if (!FolderRoles.IsValid(role)) return BadRequestEnveloppe("Unknown folder role");

        // No mailbox is opened here, but the account still has to be resolved: the override
        // rows are per account, and clearing the wrong account's is silent data loss.
        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

        await roleStore.DeleteAsync(AuthenticatedUser.WebmailUid, connection.StorageAccountId, role!, cancellationToken);
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
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folder)) return BadRequestEnveloppe("A folder is required");
        if (page < 0) return BadRequestEnveloppe("Page must not be negative");

        // An unbounded page size lets one request pull an entire mailbox.
        if (pageSize is < 1 or > 200) return BadRequestEnveloppe("Page size must be between 1 and 200");

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

        var result = await messages.ListAsync(AuthenticatedUser, connection, folder, page, pageSize, cancellationToken);

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

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

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

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

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

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

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

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

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

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

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

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

        var result = await messages.DeleteAsync(AuthenticatedUser, connection, request.FolderPath, request.Uids, cancellationToken);

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway, successStatusCode: StatusCodes.Status204NoContent);
    }

    /// <summary>Empties a whole folder: purge (no target) or move every message to a target.</summary>
    /// <param name="request">source folder and optional target (blank = purge)</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">The folder was emptied</response>
    /// <response code="400">The source is missing, the target equals the source, or the target cannot hold messages</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpPost("Folders/Empty")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> EmptyFolder(EmptyFolderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FolderPath))
            return BadRequestEnveloppe("A folder is required");
        if (!string.IsNullOrWhiteSpace(request.TargetFolderPath)
            && string.Equals(request.FolderPath, request.TargetFolderPath, StringComparison.Ordinal))
            return BadRequestEnveloppe("The target folder must differ from the source folder");

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

        var result = await messages.EmptyAsync(
            AuthenticatedUser, connection, request.FolderPath, request.TargetFolderPath, cancellationToken);

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
        if (request == null) return BadRequestEnveloppe("Request body is required");
        if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequestEnveloppe("A folder is required");
        if (request.Page < 0) return BadRequestEnveloppe("Page must not be negative");
        if (request.PageSize is < 1 or > 200) return BadRequestEnveloppe("Page size must be between 1 and 200");

        var criteria = new MailSearchCriteria(
            request.Quick, request.From, request.To, request.Subject, request.Text,
            request.SinceDays, request.Unread, request.Flagged, request.HasAttachment);
        if (!MailSearchQueryBuilder.HasAnyCriterion(criteria))
            return BadRequestEnveloppe("At least one search criterion is required");

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

        var result = await messages.SearchAsync(
            AuthenticatedUser, connection, request.FolderPath, request.AllFolders,
            criteria, request.Page, request.PageSize, cancellationToken);

        if (result.IsFailure && IsMissing(result.Error)) return NotFoundEnveloppe(result.Error);

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

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

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

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
        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

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
        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

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
        if (request == null) return BadRequestEnveloppe("Request body is required");

        var invalid = NormalizeOutgoing(request, requireRecipient: true, out var normalized);
        if (invalid != null) return invalid;
        request = normalized;

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

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

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

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

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

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

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

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

    /// <summary>
    /// The three sentinels that name a thing that is not there, against the mail server merely
    /// refusing. Rule 4 keeps them constants precisely so the layer producing an error and the
    /// layer choosing a status cannot drift apart, which is what a per-call spelling would allow.
    /// </summary>
    private static bool IsMissing(string error) =>
        error is ImapSession.FolderNotFound or ImapSession.MessageNotFound or ImapSession.AttachmentNotFound;

    private static void StampRoles(IReadOnlyList<MailFolderNode> nodes, IReadOnlyDictionary<string, string> roleByPath)
    {
        foreach (var node in nodes)
        {
            node.SpecialUse = roleByPath.TryGetValue(node.Path, out var role) ? role : null;
            StampRoles(node.Children, roleByPath);
        }
    }
}
