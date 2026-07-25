using CSharpFunctionalExtensions;
using MimeKit;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// Message access over IMAP. One session per method, opened and disposed inside it — the
/// same shape as MailFolderRepository.
/// </summary>
internal sealed class MailMessageRepository : IMailMessageRepository
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

    public async Task<Result> SetFlagsAsync(User user, string password, string folderPath, IReadOnlyList<uint> uids, MailFlag flag, bool value, CancellationToken cancellationToken)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));

        var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
        if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
        await using var session = sessionResult.Value;

        return await session.SetFlagsAsync(folderPath, uids, flag, value, cancellationToken);
    }

    public async Task<Result> MoveOrCopyAsync(User user, string password, string folderPath, IReadOnlyList<uint> uids, string targetPath, bool copy, CancellationToken cancellationToken)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));

        var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
        if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
        await using var session = sessionResult.Value;

        return await session.MoveOrCopyAsync(folderPath, uids, targetPath, copy, cancellationToken);
    }

    public async Task<Result> DeleteAsync(User user, string password, string folderPath, IReadOnlyList<uint> uids, CancellationToken cancellationToken)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));

        var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
        if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
        await using var session = sessionResult.Value;

        return await session.DeleteAsync(folderPath, uids, cancellationToken);
    }

    public async Task<Result> EmptyAsync(User user, string password, string folderPath, string? targetPath, CancellationToken cancellationToken)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));

        var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
        if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
        await using var session = sessionResult.Value;
        return await session.EmptyAsync(folderPath, targetPath, cancellationToken);
    }

    public async Task<Result<MailSearchPage>> SearchAsync(User user, string password, string folderPath, bool allFolders, MailSearchCriteria criteria, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));

        var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
        if (sessionResult.IsFailure) return Result.Failure<MailSearchPage>(sessionResult.Error);
        await using var session = sessionResult.Value;

        return await session.SearchAsync(folderPath, allFolders, criteria, page, pageSize, cancellationToken);
    }

    public async Task<Result> AppendAsync(User user, string password, string folderPath, MimeMessage message, bool seen, CancellationToken cancellationToken)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));

        var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
        if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
        await using var session = sessionResult.Value;

        return await session.AppendAsync(folderPath, message, seen, cancellationToken);
    }

    public async Task<Result<MimeMessage>> GetMimeMessageAsync(User user, string password, string folderPath, uint uid, CancellationToken cancellationToken)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));

        var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
        if (sessionResult.IsFailure) return Result.Failure<MimeMessage>(sessionResult.Error);
        await using var session = sessionResult.Value;

        return await session.GetMimeMessageAsync(folderPath, uid, cancellationToken);
    }
}
