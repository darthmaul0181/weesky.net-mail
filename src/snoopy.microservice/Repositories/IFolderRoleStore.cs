using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// Reads and writes folder-role overrides. Knows nothing about IMAP: validity against the
/// live mailbox is the resolver's business, and capturing uid_validity / mailbox_id from a
/// live folder is the caller's.
/// </summary>
public interface IFolderRoleStore
{
    Task<IReadOnlyList<FolderRoleOverride>> GetAsync(string accountId, CancellationToken cancellationToken);

    Task UpsertAsync(FolderRoleOverride @override, CancellationToken cancellationToken);

    /// <summary>Idempotent: clearing an absent override is not an error.</summary>
    Task DeleteAsync(string accountId, string role, CancellationToken cancellationToken);

    /// <summary>
    /// After a successful IMAP rename. The exact row gets the new path and the freshly
    /// re-read identity; subtree rows get their prefix swapped and keep their own
    /// identity. The separator comes from the live session — '.' on the home server,
    /// '/' elsewhere — never from a constant.
    /// </summary>
    Task ApplyRenameAsync(string accountId, string oldPath, string newPath, char separator,
        ulong newUidValidity, string? newMailboxId, CancellationToken cancellationToken);

    /// <summary>After a successful IMAP delete: purge the folder's row and its subtree's.</summary>
    Task RemoveSubtreeAsync(string accountId, string path, char separator, CancellationToken cancellationToken);
}
