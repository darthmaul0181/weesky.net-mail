using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// An open, authenticated IMAP session. One session per repository method, disposed at the
/// end of it — there is no pooling, which is also how Rainloop operates.
/// </summary>
public interface IImapSession : IAsyncDisposable
{
    /// <summary>
    /// Hierarchy separator, read from the server's personal namespace rather than
    /// configured: an additional domain may point at a server that uses a different one.
    /// </summary>
    char DirectorySeparator { get; }

    /// <summary>The full folder tree, subscribed and unsubscribed alike.</summary>
    Task<Result<IReadOnlyList<MailFolderNode>>> ListFoldersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a folder and subscribes it — a folder the user just created should be
    /// visible. Returns its full path, which only the server can compose since it owns
    /// the separator.
    /// </summary>
    Task<Result<string>> CreateFolderAsync(string parentPath, string name, CancellationToken cancellationToken);

    /// <summary>Renames a folder, optionally moving it under a different parent.</summary>
    Task<Result<string>> RenameFolderAsync(string path, string newParentPath, string newName, CancellationToken cancellationToken);

    /// <summary>Deletes a folder. Refuses the inbox.</summary>
    Task<Result> DeleteFolderAsync(string path, CancellationToken cancellationToken);

    /// <summary>Subscribes or unsubscribes a folder, which is how the UI hides it.</summary>
    Task<Result> SetSubscriptionAsync(string path, bool subscribed, CancellationToken cancellationToken);

    /// <summary>
    /// Identity snapshot of one folder, read live: path, UIDVALIDITY, MAILBOXID when the
    /// server supports OBJECTID, and selectability. Fails with ImapSession.FolderNotFound
    /// when the path no longer resolves.
    /// </summary>
    Task<Result<MailFolderStatus>> GetFolderStatusAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// One page of a folder, newest message first, envelope-level only. Carries the
    /// folder's UidValidity so the client knows when its cached UIDs went stale.
    /// </summary>
    Task<Result<MailFolderPage>> ListMessagesAsync(string folderPath, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// A single message: sanitised HTML body, plain-text body, headers and the attachment
    /// list. Fails with ImapSession.MessageNotFound when the UID no longer resolves.
    /// </summary>
    Task<Result<MailMessageDetail>> GetMessageAsync(string folderPath, uint uid, CancellationToken cancellationToken);

    /// <summary>
    /// One decoded attachment, addressed by MIME part specifier. Fails with
    /// ImapSession.MessageNotFound or ImapSession.AttachmentNotFound.
    /// </summary>
    Task<Result<MailAttachmentContent>> GetAttachmentAsync(string folderPath, uint uid, string partSpecifier, CancellationToken cancellationToken);

    /// <summary>
    /// Sets or clears a flag on a batch of UIDs. Fails with ImapSession.FolderNotFound
    /// when the path no longer resolves.
    /// </summary>
    Task<Result> SetFlagsAsync(string folderPath, IReadOnlyList<uint> uids, MailFlag flag, bool value, CancellationToken cancellationToken);

    /// <summary>
    /// Moves or copies a batch of UIDs into another folder. Fails with
    /// ImapSession.TargetNotSelectable when the target cannot hold messages, or
    /// ImapSession.FolderNotFound when the source path no longer resolves.
    /// </summary>
    Task<Result> MoveOrCopyAsync(string folderPath, IReadOnlyList<uint> uids, string targetPath, bool copy, CancellationToken cancellationToken);

    /// <summary>
    /// Permanently deletes a batch of UIDs via UID EXPUNGE. Fails with
    /// ImapSession.FolderNotFound when the path no longer resolves, or refuses when the server
    /// lacks UIDPLUS (a bare EXPUNGE would purge beyond the caller's own \Deleted messages).
    /// </summary>
    Task<Result> DeleteAsync(string folderPath, IReadOnlyList<uint> uids, CancellationToken cancellationToken);

    /// <summary>
    /// Empties a whole folder. A null/blank <paramref name="targetPath"/> purges it
    /// (mark 1:* \Deleted + EXPUNGE, no UIDPLUS needed — the whole folder is expunged, not a
    /// subset). A target moves every message there, failing with
    /// ImapSession.TargetNotSelectable when the target cannot hold messages.
    /// </summary>
    Task<Result> EmptyAsync(string folderPath, string? targetPath, CancellationToken cancellationToken);
}
