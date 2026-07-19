using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories
{
    /// <summary>
    /// Message access over IMAP. One session per method, opened and disposed inside it — the
    /// same shape as MailFolderRepository.
    /// </summary>
    public class MailMessageRepository : IMailMessageRepository
    {
        private readonly IImapConnectionFactory _factory;
        private readonly ILogger<MailMessageRepository> _logger;

        public MailMessageRepository(IImapConnectionFactory factory, ILogger<MailMessageRepository> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task<Result<MailFolderPage>> ListAsync(User user, string password, string folderPath, int page, int pageSize, CancellationToken cancellationToken)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure<MailFolderPage>(sessionResult.Error);
            await using var session = sessionResult.Value;

            return await session.ListMessagesAsync(folderPath, page, pageSize, cancellationToken);
        }

        public async Task<Result<MailMessageDetail>> GetAsync(User user, string password, string folderPath, uint uid, CancellationToken cancellationToken)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure<MailMessageDetail>(sessionResult.Error);
            await using var session = sessionResult.Value;

            return await session.GetMessageAsync(folderPath, uid, cancellationToken);
        }

        public async Task<Result<MailAttachmentContent>> GetAttachmentAsync(User user, string password, string folderPath, uint uid, string partSpecifier, CancellationToken cancellationToken)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure<MailAttachmentContent>(sessionResult.Error);
            await using var session = sessionResult.Value;

            return await session.GetAttachmentAsync(folderPath, uid, partSpecifier, cancellationToken);
        }
    }
}
