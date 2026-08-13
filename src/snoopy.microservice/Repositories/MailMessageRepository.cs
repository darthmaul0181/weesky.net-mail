using CSharpFunctionalExtensions;
using MimeKit;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// Message access over IMAP. Every method runs against the request's shared session — the same
/// shape as MailFolderRepository — so a request needing several operations pays one connection.
/// </summary>
internal sealed class MailMessageRepository(IImapSessionProvider sessions) : IMailMessageRepository
{
    public Task<Result<MailFolderPage>> ListAsync(
        User user, MailAccountConnection connection, string folderPath, int page, int pageSize,
        bool grouped, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection,
            session => session.ListMessagesAsync(folderPath, page, pageSize, grouped, cancellationToken), cancellationToken);
    }

    public Task<Result<MailMessageDetail>> GetAsync(
        User user, MailAccountConnection connection, string folderPath, uint uid, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection,
            session => session.GetMessageAsync(folderPath, uid, cancellationToken), cancellationToken);
    }

    public Task<Result<MailAttachmentContent>> GetAttachmentAsync(
        User user, MailAccountConnection connection, string folderPath, uint uid, string partSpecifier,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection,
            session => session.GetAttachmentAsync(folderPath, uid, partSpecifier, cancellationToken), cancellationToken);
    }

    public Task<Result> SetFlagsAsync(
        User user, MailAccountConnection connection, string folderPath, IReadOnlyList<uint> uids, MailFlag flag,
        bool value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection,
            session => session.SetFlagsAsync(folderPath, uids, flag, value, cancellationToken), cancellationToken);
    }

    public Task<Result> MoveOrCopyAsync(
        User user, MailAccountConnection connection, string folderPath, IReadOnlyList<uint> uids, string targetPath,
        bool copy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection,
            session => session.MoveOrCopyAsync(folderPath, uids, targetPath, copy, cancellationToken), cancellationToken);
    }

    public Task<Result> DeleteAsync(
        User user, MailAccountConnection connection, string folderPath, IReadOnlyList<uint> uids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection,
            session => session.DeleteAsync(folderPath, uids, cancellationToken), cancellationToken);
    }

    public Task<Result> EmptyAsync(
        User user, MailAccountConnection connection, string folderPath, string? targetPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection,
            session => session.EmptyAsync(folderPath, targetPath, cancellationToken), cancellationToken);
    }

    public Task<Result<MailSearchPage>> SearchAsync(
        User user, MailAccountConnection connection, string folderPath, bool allFolders, MailSearchCriteria criteria,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection,
            session => session.SearchAsync(folderPath, allFolders, criteria, page, pageSize, cancellationToken),
            cancellationToken);
    }

    public Task<Result> AppendAsync(
        User user, MailAccountConnection connection, string folderPath, MimeMessage message, bool seen,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection,
            session => session.AppendAsync(folderPath, message, seen, cancellationToken), cancellationToken);
    }

    public Task<Result<uint>> SaveDraftAsync(
        User user, MailAccountConnection connection, string folderPath, MimeMessage message, uint? replaceUid,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection,
            session => session.SaveDraftAsync(folderPath, message, replaceUid, cancellationToken), cancellationToken);
    }

    public Task<Result<MimeMessage>> GetMimeMessageAsync(
        User user, MailAccountConnection connection, string folderPath, uint uid, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection,
            session => session.GetMimeMessageAsync(folderPath, uid, cancellationToken), cancellationToken);
    }

    public Task<Result<MailMessageSource>> GetSourceAsync(
        User user, MailAccountConnection connection, string folderPath, uint uid, int maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection,
            session => session.GetMessageSourceAsync(folderPath, uid, maxBytes, cancellationToken), cancellationToken);
    }
}
