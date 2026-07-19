using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories
{
    /// <summary>
    /// Folder access over IMAP. One session per method, opened and disposed inside it — the
    /// same shape as SieveRepository over ManageSieve.
    /// </summary>
    public class MailFolderRepository : IMailFolderRepository
    {
        private readonly IImapConnectionFactory _factory;
        private readonly IFolderRoleStore _roleStore;
        private readonly ILogger<MailFolderRepository> _logger;

        public MailFolderRepository(IImapConnectionFactory factory, IFolderRoleStore roleStore, ILogger<MailFolderRepository> logger)
        {
            _factory = factory;
            _roleStore = roleStore;
            _logger = logger;
        }

        public async Task<Result<IReadOnlyList<MailFolderNode>>> GetTreeAsync(User user, string password, CancellationToken cancellationToken)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure<IReadOnlyList<MailFolderNode>>(sessionResult.Error);
            await using var session = sessionResult.Value;

            return await session.ListFoldersAsync(cancellationToken);
        }

        public async Task<Result<string>> CreateFolderAsync(User user, string password, string parentPath, string name, CancellationToken cancellationToken)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure<string>(sessionResult.Error);
            await using var session = sessionResult.Value;

            return await session.CreateFolderAsync(parentPath, name, cancellationToken);
        }

        public async Task<Result<string>> RenameFolderAsync(User user, string password, string path, string newParentPath, string newName, CancellationToken cancellationToken)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure<string>(sessionResult.Error);
            await using var session = sessionResult.Value;

            var renamed = await session.RenameFolderAsync(path, newParentPath, newName, cancellationToken);
            if (renamed.IsFailure) return renamed;

            await TryMoveOverridesAsync(session, user, path, renamed.Value, cancellationToken);
            return renamed;
        }

        /// <summary>
        /// IMAP first, database second. If this bookkeeping fails, the stored overrides go
        /// stale and the resolver's staleness guard degrades them to discovery — the rename
        /// the user asked for is never failed over it. The identity is re-read from the
        /// renamed folder, not carried over: some servers change UIDVALIDITY on rename, and
        /// carrying the old value would make our own rename trip our own guard.
        /// </summary>
        private async Task TryMoveOverridesAsync(IImapSession session, User user, string oldPath, string newPath, CancellationToken cancellationToken)
        {
            try
            {
                var status = await session.GetFolderStatusAsync(newPath, cancellationToken);
                if (status.IsFailure)
                {
                    _logger.LogWarning(
                        "Rename of {OldPath} succeeded but the status re-read failed: {Error}. Overrides left to the staleness guard.",
                        oldPath, status.Error);
                    return;
                }

                await _roleStore.ApplyRenameAsync(
                    FolderRoleStore.CanonicalAccountId(user.Email),
                    oldPath, newPath, session.DirectorySeparator,
                    status.Value.UidValidity, status.Value.MailboxId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to move folder role overrides after renaming {OldPath}", oldPath);
            }
        }

        public async Task<Result> DeleteFolderAsync(User user, string password, string path, CancellationToken cancellationToken)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
            await using var session = sessionResult.Value;

            var result = await session.DeleteFolderAsync(path, cancellationToken);
            if (result.IsFailure) return result;

            try
            {
                await _roleStore.RemoveSubtreeAsync(
                    FolderRoleStore.CanonicalAccountId(user.Email), path, session.DirectorySeparator, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to purge folder role overrides after deleting {Path}", path);
            }

            return result;
        }

        public async Task<Result> SetSubscriptionAsync(User user, string password, string path, bool subscribed, CancellationToken cancellationToken)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
            await using var session = sessionResult.Value;

            return await session.SetSubscriptionAsync(path, subscribed, cancellationToken);
        }

        public async Task<Result<MailFolderStatus>> GetFolderStatusAsync(User user, string password, string path, CancellationToken cancellationToken)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure<MailFolderStatus>(sessionResult.Error);
            await using var session = sessionResult.Value;

            return await session.GetFolderStatusAsync(path, cancellationToken);
        }
    }
}
