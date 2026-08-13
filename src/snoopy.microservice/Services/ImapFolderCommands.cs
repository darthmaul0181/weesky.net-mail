using CSharpFunctionalExtensions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The folder half of the IMAP protocol adapter: tree listing, CRUD, subscription, status and
/// emptying. Runs every operation through the owning session's ExecuteAsync so the failure
/// contract — cancellation, sentinels, the opaque message — stays in one place.
/// </summary>
internal sealed class ImapFolderCommands(ImapSession session, ImapClient client, ILogger logger)
{
    public Task<Result<IReadOnlyList<MailFolderNode>>> ListFoldersAsync(CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            var personal = client.PersonalNamespaces[0];

            // Asking for a STATUS item the server never advertised is a protocol error, so
            // the capability gates the request itself, not just the reading of the result.
            var statusItems = StatusItems.Count | StatusItems.Unread | StatusItems.UidValidity
                              | StatusItems.UidNext;
            if (client.Capabilities.HasFlag(ImapCapabilities.ObjectID))
                statusItems |= StatusItems.MailboxId;
            var condStore = client.Capabilities.HasFlag(ImapCapabilities.CondStore);
            if (condStore)
                statusItems |= StatusItems.HighestModSeq;

            var folders = await client.GetFoldersAsync(personal, statusItems, subscribedOnly: false, cancellationToken);

            var nodes = new Dictionary<string, MailFolderNode>(StringComparer.Ordinal);
            var roots = new List<MailFolderNode>();

            // Ordinal sort puts a parent before its children, so the lookup below always
            // finds the parent already built.
            var ordered = folders.OrderBy(f => f.FullName, StringComparer.Ordinal).ToList();

            foreach (var folder in ordered)
            {
                var selectable = (folder.Attributes & FolderAttributes.NonExistent) == 0
                                 && (folder.Attributes & FolderAttributes.NoSelect) == 0;

                var node = new MailFolderNode
                {
                    Path = folder.FullName,
                    Name = folder.Name,
                    // Deliberately left null: SpecialUse is the *chain's* output, and the
                    // chain needs the stored overrides this layer has no business reading.
                    // Resolving it here produced a value every caller had to overwrite or
                    // ignore, and would have handed the next caller un-overridden roles
                    // with nothing failing. AttributeRole carries the raw server flag,
                    // which is all FolderRoleResolver needs as input.
                    SpecialUse = null,
                    AttributeRole = SpecialUseCatalog.SpecialUseFromAttributes(folder.Attributes, IsInbox(folder)),
                    MailboxId = folder.Id,
                    Selectable = selectable,
                    Subscribed = folder.IsSubscribed,
                    Total = selectable ? folder.Count : null,
                    Unread = selectable ? folder.Unread : null,
                    UidValidity = folder.UidValidity,
                    UidNext = selectable ? folder.UidNext?.Id : null,
                    HighestModSeq = selectable && condStore ? folder.HighestModSeq : null
                };

                nodes[folder.FullName] = node;

                var parentPath = ImapSession.ParentPath(folder.FullName, session.DirectorySeparator);
                if (parentPath != null && nodes.TryGetValue(parentPath, out var parent))
                {
                    parent.Children.Add(node);
                }
                else
                {
                    roots.Add(node);
                }
            }

            return Result.Success<IReadOnlyList<MailFolderNode>>(roots);
        },
            "Unable to read the mailbox folders",
            ex => logger.LogError(ex, "Failed to list IMAP folders"));

    public Task<Result<string>> CreateFolderAsync(string parentPath, string name, CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            if (!ImapSession.IsValidLeafName(name, session.DirectorySeparator))
            {
                return Result.Failure<string>(ImapSession.InvalidFolderName);
            }

            var parent = string.IsNullOrEmpty(parentPath)
                ? client.GetFolder(client.PersonalNamespaces[0])
                : await client.GetFolderAsync(parentPath, cancellationToken);

            var created = await parent.CreateAsync(name, isMessageFolder: true, cancellationToken);
            if (created == null) return Result.Failure<string>("Unable to create the folder");

            // A folder the user just created should show up without a second step.
            await created.SubscribeAsync(cancellationToken);

            return Result.Success(created.FullName);
        },
            "Unable to create the folder",
            ex => logger.LogError(ex, "Failed to create folder {Name} under {Parent}", name, parentPath));

    public Task<Result<string>> RenameFolderAsync(string path, string newParentPath, string newName, CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            if (!ImapSession.IsValidLeafName(newName, session.DirectorySeparator))
            {
                return Result.Failure<string>(ImapSession.InvalidFolderName);
            }

            var folder = await client.GetFolderAsync(path, cancellationToken);
            var newParent = string.IsNullOrEmpty(newParentPath)
                ? client.GetFolder(client.PersonalNamespaces[0])
                : await client.GetFolderAsync(newParentPath, cancellationToken);

            await folder.RenameAsync(newParent, newName, cancellationToken);

            return Result.Success(folder.FullName);
        },
            "Unable to rename the folder",
            ex => logger.LogError(ex, "Failed to rename folder {Path}", path));

    public Task<Result> DeleteFolderAsync(string path, CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await client.GetFolderAsync(path, cancellationToken);

            if ((folder.Attributes & FolderAttributes.Inbox) != 0)
            {
                return Result.Failure("The inbox cannot be deleted");
            }

            await folder.DeleteAsync(cancellationToken);

            return Result.Success();
        },
            "Unable to delete the folder",
            ex => logger.LogError(ex, "Failed to delete folder {Path}", path));

    public Task<Result> SetSubscriptionAsync(string path, bool subscribed, CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await client.GetFolderAsync(path, cancellationToken);

            if (subscribed) await folder.SubscribeAsync(cancellationToken);
            else await folder.UnsubscribeAsync(cancellationToken);

            return Result.Success();
        },
            "Unable to change the folder visibility",
            ex => logger.LogError(ex, "Failed to set subscription on {Path}", path));

    public Task<Result<MailFolderStatus>> GetFolderStatusAsync(string path, CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await client.GetFolderAsync(path, cancellationToken);

            var selectable = (folder.Attributes & FolderAttributes.NonExistent) == 0
                             && (folder.Attributes & FolderAttributes.NoSelect) == 0;

            // STATUS on a \NoSelect folder is a protocol error; the caller rejects the
            // folder on Selectable alone, so there is nothing more to read.
            if (!selectable)
            {
                return Result.Success(new MailFolderStatus { Path = folder.FullName, Selectable = false });
            }

            var items = StatusItems.UidValidity;
            if (client.Capabilities.HasFlag(ImapCapabilities.ObjectID))
                items |= StatusItems.MailboxId;
            await folder.StatusAsync(items, cancellationToken);

            return Result.Success(new MailFolderStatus
            {
                Path = folder.FullName,
                UidValidity = folder.UidValidity,
                MailboxId = folder.Id,
                Selectable = true
            });
        },
            "Unable to read the folder",
            ex => logger.LogError(ex, "Failed to read the status of {Folder}", path),
            ImapSession.FolderSentinel);

    /// <summary>
    /// GETQUOTAROOT INBOX. RFC 2087 reports STORAGE in 1024-octet blocks; the model is
    /// bytes, so both storage figures are scaled up. A resource the server did not return
    /// at all comes back null from MailKit — mapped to 0, the model's "no limit" convention.
    /// </summary>
    public Task<Result<Quota>> GetQuotaAsync(CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            var quota = await client.Inbox.GetQuotaAsync(cancellationToken);

            return Result.Success(new Quota
            {
                StorageBytesUsed = (long)(quota.CurrentStorageSize ?? 0) * 1024,
                StorageBytesLimit = (long)(quota.StorageLimit ?? 0) * 1024,
                MessageCount = quota.CurrentMessageCount ?? 0,
                MessageLimit = quota.MessageLimit ?? 0
            });
        },
            "Unable to read the mailbox quota",
            ex => logger.LogError(ex, "Failed to read the mailbox quota"));

    public Task<Result> EmptyAsync(string folderPath, string? targetPath, CancellationToken cancellationToken)
    {
        // Read before the operation, not inside it: the failure log below is a sibling argument
        // and cannot see the operation's locals.
        var move = !string.IsNullOrWhiteSpace(targetPath);

        return session.ExecuteAsync(cancellationToken, async () =>
        {
            IMailFolder? target = null;
            if (move)
            {
                var targetResult = await ImapSession.ResolveTargetOrFailAsync(client, targetPath!, cancellationToken);
                if (targetResult.IsFailure) return Result.Failure(targetResult.Error);
                target = targetResult.Value;
            }

            var folder = await client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            if (move)
            {
                var uids = await folder.SearchAsync(SearchQuery.All, cancellationToken);
                if (uids.Count == 0) return Result.Success();

                await folder.MoveToAsync(uids, target!, cancellationToken);
            }
            else
            {
                // 1:* needs no SEARCH: enumerating a large trash into an explicit UID set only
                // named every message the range already names, on a command line that grew with
                // the folder. Bare EXPUNGE purges every \Deleted message; emptying purges the
                // whole folder, so no UID EXPUNGE (UIDPLUS) is needed — unlike DeleteAsync.
                await folder.AddFlagsAsync(new UniqueIdRange(UniqueId.MinValue, UniqueId.MaxValue),
                    MessageFlags.Deleted, silent: true, cancellationToken);
                await folder.ExpungeAsync(cancellationToken);
            }

            return Result.Success();
        },
            "Unable to empty the folder",
            ex => logger.LogError(ex, "Failed to empty {Folder} (move: {Move})", folderPath, move),
            ImapSession.FolderSentinel);
    }

    private static bool IsInbox(IMailFolder folder)
        => string.Equals(folder.FullName, "INBOX", StringComparison.OrdinalIgnoreCase);
}
