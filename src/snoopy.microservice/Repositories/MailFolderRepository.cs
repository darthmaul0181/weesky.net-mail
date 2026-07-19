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
        private readonly ILogger<MailFolderRepository> _logger;

        public MailFolderRepository(IImapConnectionFactory factory, ILogger<MailFolderRepository> logger)
        {
            _factory = factory;
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

            return await session.RenameFolderAsync(path, newParentPath, newName, cancellationToken);
        }

        public async Task<Result> DeleteFolderAsync(User user, string password, string path, CancellationToken cancellationToken)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
            await using var session = sessionResult.Value;

            return await session.DeleteFolderAsync(path, cancellationToken);
        }

        public async Task<Result> SetSubscriptionAsync(User user, string password, string path, bool subscribed, CancellationToken cancellationToken)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
            await using var session = sessionResult.Value;

            return await session.SetSubscriptionAsync(path, subscribed, cancellationToken);
        }
    }
}
