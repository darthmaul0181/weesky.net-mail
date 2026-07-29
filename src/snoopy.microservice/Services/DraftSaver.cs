using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Orchestrates a draft save: build the message via the shared factory, resolve the drafts-role
/// folder, then APPEND-with-replace. Unlike the best-effort Sent copy, the APPEND here is the whole
/// point of the request, so a missing drafts folder is a failure, not a degraded success.
/// </summary>
internal sealed class DraftSaver(
    IOutgoingMessageFactory factory,
    IMailFolderRepository folders,
    IFolderRoleStore roles,
    IMailMessageRepository messages,
    ILogger<DraftSaver> logger) : IDraftSaver
{
    public async Task<Result<SavedDraft>> SaveAsync(
        User user, MailAccountConnection connection, SaveDraftRequest request, CancellationToken cancellationToken)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));

        var built = await factory.CreateAsync(user, connection, request, cancellationToken);
        if (built.IsFailure) return Result.Failure<SavedDraft>(built.Error);

        var tree = await folders.GetTreeAsync(user, connection, cancellationToken);
        if (tree.IsFailure) return Result.Failure<SavedDraft>(tree.Error);

        // A preferences outage must not block a save the SPECIAL-USE flags can already place.
        IReadOnlyList<FolderRoleOverride> overrides;
        try
        {
            overrides = await roles.GetAsync(user.WebmailUid, connection.StorageAccountId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Role overrides unavailable for {UserId}: using server flags", user.WebmailUid);
            overrides = [];
        }

        var drafts = FolderRoleResolver.Resolve(tree.Value, overrides).Roles
            .FirstOrDefault(r => r.Role == "drafts" && r.FolderPath != null);
        // Unlike the Sent copy this APPEND is the whole point of the request: no folder is a failure.
        if (drafts == null)
        {
            logger.LogWarning("Draft not saved: no folder holds the drafts role for {UserId}", user.WebmailUid);
            return Result.Failure<SavedDraft>(IDraftSaver.NoDraftsFolder);
        }

        var saved = await messages.SaveDraftAsync(
            user, connection, drafts.FolderPath!, built.Value, request.ReplaceUid, cancellationToken);
        if (saved.IsFailure) return Result.Failure<SavedDraft>(saved.Error);

        // Staged files stay: the composer is still open on them for the next save or the send.
        return Result.Success(new SavedDraft(saved.Value, drafts.FolderPath!));
    }
}
