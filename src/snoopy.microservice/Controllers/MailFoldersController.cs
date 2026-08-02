using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// The folder side of the mailbox: the tree, folder lifecycle, subscription, emptying,
/// and the well-known-role assignments.
/// </summary>
// The route is spelled out rather than [controller]: four classes serve the historical
// api/Mail prefix, and a class-name-derived route would silently move every URL.
[Route("api/Mail")]
[ApiController]
[Authorize]
public sealed class MailFoldersController(
    IMailFolderRepository folders,
    IMailMessageRepository messages,
    IAccountConnectionResolver connections,
    IFolderRoleStore roleStore) : MailControllerBase(connections)
{
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
        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var result = await folders.GetTreeAsync(AuthenticatedUser, connection, cancellationToken);
        if (result.IsFailure)
            return BadGatewayEnveloppe(result.Error);

        // The tree's SpecialUse is the resolution chain's output, not raw discovery: a
        // user override reassigns the role, and the displaced folder shows under its own
        // name (spec § 4.1).
        var overrides = await roleStore.GetAsync(AuthenticatedUser.WebmailUid, connection.StorageAccountId, cancellationToken);
        var roles = FolderRoleResolver.Resolve(result.Value, overrides);
        StampRoles(result.Value, roles.RoleByPath);

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
        // Unreachable behind model binding, which refuses first with the identical message —
        // kept as defence in depth, and as the path the direct-invocation tests exercise.
        if (request == null) return BadRequestEnveloppe("Request body is required");
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequestEnveloppe("A folder name is required");

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

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

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

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

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

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

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

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

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var result = await messages.EmptyAsync(
            AuthenticatedUser, connection, request.FolderPath, request.TargetFolderPath, cancellationToken);

        if (result.IsFailure && result.Error == ImapSession.TargetNotSelectable)
            return BadRequestEnveloppe("The target folder cannot hold messages");

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway, successStatusCode: StatusCodes.Status204NoContent);
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
        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var tree = await folders.GetTreeAsync(AuthenticatedUser, connection, cancellationToken);
        if (tree.IsFailure)
            return BadGatewayEnveloppe(tree.Error);

        var overrides = await roleStore.GetAsync(AuthenticatedUser.WebmailUid, connection.StorageAccountId, cancellationToken);
        var roles = FolderRoleResolver.Resolve(tree.Value, overrides);

        return Ok(roles.Roles);
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

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

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
        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        await roleStore.DeleteAsync(AuthenticatedUser.WebmailUid, connection.StorageAccountId, role!, cancellationToken);
        return NoContent();
    }

    private static void StampRoles(IReadOnlyList<MailFolderNode> nodes, IReadOnlyDictionary<string, string> roleByPath)
    {
        foreach (var node in nodes)
        {
            node.SpecialUse = roleByPath.TryGetValue(node.Path, out var role) ? role : null;
            StampRoles(node.Children, roleByPath);
        }
    }
}
