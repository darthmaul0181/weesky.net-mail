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
        User user, string password, string folderPath, int page, int pageSize, CancellationToken cancellationToken) =>
        sessions.WithSessionAsync(user, password,
            session => session.ListMessagesAsync(folderPath, page, pageSize, cancellationToken), cancellationToken);

    public Task<Result<MailMessageDetail>> GetAsync(
        User user, string password, string folderPath, uint uid, CancellationToken cancellationToken) =>
        sessions.WithSessionAsync(user, password,
            session => session.GetMessageAsync(folderPath, uid, cancellationToken), cancellationToken);

    public Task<Result<MailAttachmentContent>> GetAttachmentAsync(
        User user, string password, string folderPath, uint uid, string partSpecifier, CancellationToken cancellationToken) =>
        sessions.WithSessionAsync(user, password,
            session => session.GetAttachmentAsync(folderPath, uid, partSpecifier, cancellationToken), cancellationToken);

    public Task<Result> SetFlagsAsync(
        User user, string password, string folderPath, IReadOnlyList<uint> uids, MailFlag flag, bool value,
        CancellationToken cancellationToken) =>
        sessions.WithSessionAsync(user, password,
            session => session.SetFlagsAsync(folderPath, uids, flag, value, cancellationToken), cancellationToken);

    public Task<Result> MoveOrCopyAsync(
        User user, string password, string folderPath, IReadOnlyList<uint> uids, string targetPath, bool copy,
        CancellationToken cancellationToken) =>
        sessions.WithSessionAsync(user, password,
            session => session.MoveOrCopyAsync(folderPath, uids, targetPath, copy, cancellationToken), cancellationToken);

    public Task<Result> DeleteAsync(
        User user, string password, string folderPath, IReadOnlyList<uint> uids, CancellationToken cancellationToken) =>
        sessions.WithSessionAsync(user, password,
            session => session.DeleteAsync(folderPath, uids, cancellationToken), cancellationToken);

    public Task<Result> EmptyAsync(
        User user, string password, string folderPath, string? targetPath, CancellationToken cancellationToken) =>
        sessions.WithSessionAsync(user, password,
            session => session.EmptyAsync(folderPath, targetPath, cancellationToken), cancellationToken);

    public Task<Result<MailSearchPage>> SearchAsync(
        User user, string password, string folderPath, bool allFolders, MailSearchCriteria criteria,
        int page, int pageSize, CancellationToken cancellationToken) =>
        sessions.WithSessionAsync(user, password,
            session => session.SearchAsync(folderPath, allFolders, criteria, page, pageSize, cancellationToken),
            cancellationToken);

    public Task<Result> AppendAsync(
        User user, string password, string folderPath, MimeMessage message, bool seen, CancellationToken cancellationToken) =>
        sessions.WithSessionAsync(user, password,
            session => session.AppendAsync(folderPath, message, seen, cancellationToken), cancellationToken);

    public Task<Result<uint>> SaveDraftAsync(
        User user, string password, string folderPath, MimeMessage message, uint? replaceUid,
        CancellationToken cancellationToken) =>
        sessions.WithSessionAsync(user, password,
            session => session.SaveDraftAsync(folderPath, message, replaceUid, cancellationToken), cancellationToken);

    public Task<Result<MimeMessage>> GetMimeMessageAsync(
        User user, string password, string folderPath, uint uid, CancellationToken cancellationToken) =>
        sessions.WithSessionAsync(user, password,
            session => session.GetMimeMessageAsync(folderPath, uid, cancellationToken), cancellationToken);
}
