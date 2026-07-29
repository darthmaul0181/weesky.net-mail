using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// Folder access over IMAP. Every method runs against the request's shared session, so a
/// rename — which reads the tree first to refuse a system folder — no longer opens two
/// connections to do one thing.
/// </summary>
internal sealed class MailFolderRepository(
    IImapSessionProvider sessions, IFolderRoleStore roleStore, ILogger<MailFolderRepository> logger)
    : IMailFolderRepository
{
    public Task<Result<IReadOnlyList<MailFolderNode>>> GetTreeAsync(
        User user, MailAccountConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection,
            session => session.ListFoldersAsync(cancellationToken), cancellationToken);
    }

    public Task<Result<string>> CreateFolderAsync(
        User user, MailAccountConnection connection, string parentPath, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection,
            session => session.CreateFolderAsync(parentPath, name, cancellationToken), cancellationToken);
    }

    public Task<Result<string>> RenameFolderAsync(
        User user, MailAccountConnection connection, string path, string newParentPath, string newName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection, async session =>
        {
            var renamed = await session.RenameFolderAsync(path, newParentPath, newName, cancellationToken);
            if (renamed.IsFailure) return renamed;

            await TryMoveOverridesAsync(session, user, connection.StorageAccountId, path, renamed.Value, cancellationToken);
            return renamed;
        }, cancellationToken);
    }

    /// <summary>
    /// IMAP first, database second. If this bookkeeping fails, the stored overrides go
    /// stale and the resolver's staleness guard degrades them to discovery — the rename
    /// the user asked for is never failed over it. The identity is re-read from the
    /// renamed folder, not carried over: some servers change UIDVALIDITY on rename, and
    /// carrying the old value would make our own rename trip our own guard.
    /// </summary>
    private async Task TryMoveOverridesAsync(
        IImapSession session, User user, string accountId, string oldPath, string newPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var status = await session.GetFolderStatusAsync(newPath, cancellationToken);
            if (status.IsFailure)
            {
                logger.LogWarning(
                    "Rename of {OldPath} succeeded but the status re-read failed: {Error}. Overrides left to the staleness guard.",
                    oldPath, status.Error);
                return;
            }

            await roleStore.ApplyRenameAsync(
                user.WebmailUid, accountId, oldPath, newPath, session.DirectorySeparator,
                status.Value.UidValidity, status.Value.MailboxId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to move folder role overrides after renaming {OldPath}", oldPath);
        }
    }

    public Task<Result> DeleteFolderAsync(
        User user, MailAccountConnection connection, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection, async session =>
        {
            var result = await session.DeleteFolderAsync(path, cancellationToken);
            if (result.IsFailure) return result;

            try
            {
                await roleStore.RemoveSubtreeAsync(
                    user.WebmailUid, connection.StorageAccountId, path, session.DirectorySeparator, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to purge folder role overrides after deleting {Path}", path);
            }

            return result;
        }, cancellationToken);
    }

    public Task<Result> SetSubscriptionAsync(
        User user, MailAccountConnection connection, string path, bool subscribed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection,
            session => session.SetSubscriptionAsync(path, subscribed, cancellationToken), cancellationToken);
    }

    public Task<Result<MailFolderStatus>> GetFolderStatusAsync(
        User user, MailAccountConnection connection, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection,
            session => session.GetFolderStatusAsync(path, cancellationToken), cancellationToken);
    }
}
