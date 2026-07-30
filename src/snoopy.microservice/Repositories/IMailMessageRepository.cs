using CSharpFunctionalExtensions;
using MimeKit;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Repositories;

public interface IMailMessageRepository
{
    /// <summary>One page of a folder, newest message first.</summary>
    Task<Result<MailFolderPage>> ListAsync(User user, MailAccountConnection connection, string folderPath, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>A single message, body sanitised and attachments listed.</summary>
    Task<Result<MailMessageDetail>> GetAsync(User user, MailAccountConnection connection, string folderPath, uint uid, CancellationToken cancellationToken);

    /// <summary>One decoded attachment, addressed by MIME part specifier.</summary>
    Task<Result<MailAttachmentContent>> GetAttachmentAsync(User user, MailAccountConnection connection, string folderPath, uint uid, string partSpecifier, CancellationToken cancellationToken);

    /// <summary>Sets or clears a flag on a batch of UIDs.</summary>
    Task<Result> SetFlagsAsync(User user, MailAccountConnection connection, string folderPath, IReadOnlyList<uint> uids, MailFlag flag, bool value, CancellationToken cancellationToken);

    /// <summary>Moves or copies a batch of UIDs into another folder.</summary>
    Task<Result> MoveOrCopyAsync(User user, MailAccountConnection connection, string folderPath, IReadOnlyList<uint> uids, string targetPath, bool copy, CancellationToken cancellationToken);

    /// <summary>Permanently deletes a batch of UIDs via UID EXPUNGE.</summary>
    Task<Result> DeleteAsync(User user, MailAccountConnection connection, string folderPath, IReadOnlyList<uint> uids, CancellationToken cancellationToken);

    /// <summary>Empties a whole folder: purge (no target) or move every message to a target.</summary>
    Task<Result> EmptyAsync(User user, MailAccountConnection connection, string folderPath, string? targetPath, CancellationToken cancellationToken);

    /// <summary>One page of search results across one folder or the whole mailbox.</summary>
    Task<Result<MailSearchPage>> SearchAsync(User user, MailAccountConnection connection, string folderPath, bool allFolders, MailSearchCriteria criteria, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Appends a message to a folder, optionally marked read.</summary>
    Task<Result> AppendAsync(User user, MailAccountConnection connection, string folderPath, MimeMessage message, bool seen, CancellationToken cancellationToken);

    /// <summary>Appends a draft (\Draft \Seen), replacing the previous version when given. Returns the new UID.</summary>
    Task<Result<uint>> SaveDraftAsync(User user, MailAccountConnection connection, string folderPath, MimeMessage message, uint? replaceUid, CancellationToken cancellationToken);

    /// <summary>The raw MimeKit message, for quoting: unsanitised body, cid parts, attachments.</summary>
    Task<Result<MimeMessage>> GetMimeMessageAsync(User user, MailAccountConnection connection, string folderPath, uint uid, CancellationToken cancellationToken);

    /// <summary>The message as it arrived, capped at <paramref name="maxBytes"/> octets.</summary>
    Task<Result<MailMessageSource>> GetSourceAsync(User user, MailAccountConnection connection, string folderPath, uint uid, int maxBytes, CancellationToken cancellationToken);
}
