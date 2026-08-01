using System.Text;
using CSharpFunctionalExtensions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;
using MimeKit.Utils;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

internal sealed class ImapSession : IImapSession
{
    private readonly ImapClient _client;
    private readonly IMailHtmlSanitizer _sanitizer;
    private readonly ILogger _logger;
    private bool _disposed;

    public ImapSession(ImapClient client, IMailHtmlSanitizer sanitizer, ILogger logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        DirectorySeparator = client.PersonalNamespaces.Count > 0
            ? client.PersonalNamespaces[0].DirectorySeparator
            : '/';
    }

    public char DirectorySeparator { get; }

    /// <summary>
    /// The failure contract every operation on this session shares, and the reason none of them
    /// spells it out any more: a cancellation the caller asked for propagates untouched, a known
    /// IMAP condition becomes the sentinel both layers agree on, and anything else is logged in
    /// full while the client gets one opaque sentence — the server's own words never reach it.
    ///
    /// <c>sentinel</c> maps an exception to a shared sentinel, or null when it is not one this
    /// operation recognises. Deliberately per-operation rather than global: an operation scoped
    /// to a folder passes <see cref="FolderSentinel"/>, one that also resolves a specific message
    /// by UID passes <see cref="FolderOrMessageSentinel"/>, and every method now makes that
    /// choice — none is left translating a vanished folder into the opaque failure message.
    /// </summary>
    internal async Task<Result<T>> ExecuteAsync<T>(
        CancellationToken cancellationToken,
        Func<Task<Result<T>>> operation,
        string failureMessage,
        Action<Exception> logFailure,
        Func<Exception, string?>? sentinel = null)
    {
        ThrowIfDisposed();

        try
        {
            return await operation();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (sentinel?.Invoke(ex) is { } known) return Result.Failure<T>(known);

            logFailure(ex);
            return Result.Failure<T>(failureMessage);
        }
    }

    /// <inheritdoc cref="ExecuteAsync{T}"/>
    internal async Task<Result> ExecuteAsync(
        CancellationToken cancellationToken,
        Func<Task<Result>> operation,
        string failureMessage,
        Action<Exception> logFailure,
        Func<Exception, string?>? sentinel = null)
    {
        ThrowIfDisposed();

        try
        {
            return await operation();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (sentinel?.Invoke(ex) is { } known) return Result.Failure(known);

            logFailure(ex);
            return Result.Failure(failureMessage);
        }
    }

    internal static string? FolderSentinel(Exception ex) =>
        ex is FolderNotFoundException ? FolderNotFound : null;

    internal static string? MessageSentinel(Exception ex) =>
        ex is MessageNotFoundException ? MessageNotFound : null;

    internal static string? FolderOrMessageSentinel(Exception ex) =>
        FolderSentinel(ex) ?? MessageSentinel(ex);

    public Task<Result<IReadOnlyList<MailFolderNode>>> ListFoldersAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            var personal = _client.PersonalNamespaces[0];

            // Asking for a STATUS item the server never advertised is a protocol error, so
            // the capability gates the request itself, not just the reading of the result.
            var statusItems = StatusItems.Count | StatusItems.Unread | StatusItems.UidValidity
                              | StatusItems.UidNext;
            if (_client.Capabilities.HasFlag(ImapCapabilities.ObjectID))
                statusItems |= StatusItems.MailboxId;
            var condStore = _client.Capabilities.HasFlag(ImapCapabilities.CondStore);
            if (condStore)
                statusItems |= StatusItems.HighestModSeq;

            var folders = await _client.GetFoldersAsync(personal, statusItems, subscribedOnly: false, cancellationToken);

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
                    AttributeRole = SpecialUseFromAttributes(folder.Attributes, IsInbox(folder)),
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

                var parentPath = ParentPath(folder.FullName, DirectorySeparator);
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
            ex => _logger.LogError(ex, "Failed to list IMAP folders"));

    public Task<Result<string>> CreateFolderAsync(string parentPath, string name, CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            if (!IsValidLeafName(name, DirectorySeparator))
            {
                return Result.Failure<string>($"A folder name cannot be empty or contain '{DirectorySeparator}'");
            }

            var parent = string.IsNullOrEmpty(parentPath)
                ? _client.GetFolder(_client.PersonalNamespaces[0])
                : await _client.GetFolderAsync(parentPath, cancellationToken);

            var created = await parent.CreateAsync(name, isMessageFolder: true, cancellationToken);
            if (created == null) return Result.Failure<string>("Unable to create the folder");

            // A folder the user just created should show up without a second step.
            await created.SubscribeAsync(cancellationToken);

            return Result.Success(created.FullName);
        },
            "Unable to create the folder",
            ex => _logger.LogError(ex, "Failed to create folder {Name} under {Parent}", name, parentPath));

    public Task<Result<string>> RenameFolderAsync(string path, string newParentPath, string newName, CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            if (!IsValidLeafName(newName, DirectorySeparator))
            {
                return Result.Failure<string>($"A folder name cannot be empty or contain '{DirectorySeparator}'");
            }

            var folder = await _client.GetFolderAsync(path, cancellationToken);
            var newParent = string.IsNullOrEmpty(newParentPath)
                ? _client.GetFolder(_client.PersonalNamespaces[0])
                : await _client.GetFolderAsync(newParentPath, cancellationToken);

            await folder.RenameAsync(newParent, newName, cancellationToken);

            return Result.Success(folder.FullName);
        },
            "Unable to rename the folder",
            ex => _logger.LogError(ex, "Failed to rename folder {Path}", path));

    public Task<Result> DeleteFolderAsync(string path, CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await _client.GetFolderAsync(path, cancellationToken);

            if ((folder.Attributes & FolderAttributes.Inbox) != 0)
            {
                return Result.Failure("The inbox cannot be deleted");
            }

            await folder.DeleteAsync(cancellationToken);

            return Result.Success();
        },
            "Unable to delete the folder",
            ex => _logger.LogError(ex, "Failed to delete folder {Path}", path));

    public Task<Result> SetSubscriptionAsync(string path, bool subscribed, CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await _client.GetFolderAsync(path, cancellationToken);

            if (subscribed) await folder.SubscribeAsync(cancellationToken);
            else await folder.UnsubscribeAsync(cancellationToken);

            return Result.Success();
        },
            "Unable to change the folder visibility",
            ex => _logger.LogError(ex, "Failed to set subscription on {Path}", path));

    public Task<Result> SetFlagsAsync(string folderPath, IReadOnlyList<uint> uids, MailFlag flag, bool value, CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
            // First ReadWrite open of the project: every read path stays ReadOnly.
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var messageFlags = flag == MailFlag.Seen ? MessageFlags.Seen : MessageFlags.Flagged;
            var ids = uids.Select(uid => new UniqueId(uid)).ToList();

            // A UID that no longer exists is a silent server-side no-op: the batch never fails partially.
            if (value) await folder.AddFlagsAsync(ids, messageFlags, silent: true, cancellationToken);
            else await folder.RemoveFlagsAsync(ids, messageFlags, silent: true, cancellationToken);

            return Result.Success();
        },
            "Unable to update the messages",
            ex => _logger.LogError(ex, "Failed to set {Flag}={Value} on {Count} messages in {Folder}", flag, value, uids.Count, folderPath),
            FolderSentinel);

    public const string TargetNotSelectable = "target_not_selectable";

    public Task<Result> MoveOrCopyAsync(string folderPath, IReadOnlyList<uint> uids, string targetPath, bool copy, CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            var targetResult = await ResolveTargetOrFailAsync(targetPath, cancellationToken);
            if (targetResult.IsFailure) return Result.Failure(targetResult.Error);
            var target = targetResult.Value;

            var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var ids = uids.Select(uid => new UniqueId(uid)).ToList();
            // MailKit uses MOVE when advertised and falls back to COPY + \Deleted + EXPUNGE itself.
            if (copy) await folder.CopyToAsync(ids, target, cancellationToken);
            else await folder.MoveToAsync(ids, target, cancellationToken);

            return Result.Success();
        },
            copy ? "Unable to copy the messages" : "Unable to move the messages",
            ex => _logger.LogError(ex, "Failed to {Verb} {Count} messages from {Folder} to {Target}",
                copy ? "copy" : "move", uids.Count, folderPath, targetPath),
            FolderSentinel);

    /// <summary>
    /// Resolves a move/empty target, failing with <see cref="TargetNotSelectable"/> when it
    /// does not exist or is a \NoSelect container — a folder that cannot hold messages.
    /// Shared by <see cref="MoveOrCopyAsync"/> and <see cref="EmptyAsync"/> so the two never
    /// drift apart on what "selectable" means.
    /// </summary>
    private async Task<Result<IMailFolder>> ResolveTargetOrFailAsync(string targetPath, CancellationToken cancellationToken)
    {
        IMailFolder target;
        try { target = await _client.GetFolderAsync(targetPath, cancellationToken); }
        catch (FolderNotFoundException) { return Result.Failure<IMailFolder>(TargetNotSelectable); }

        // A \NoSelect container cannot hold messages; refusing here beats a server error the
        // client cannot word. Checked by the session because the controller has no tree.
        if ((target.Attributes & (FolderAttributes.NoSelect | FolderAttributes.NonExistent)) != 0)
            return Result.Failure<IMailFolder>(TargetNotSelectable);

        return Result.Success(target);
    }

    public Task<Result> DeleteAsync(string folderPath, IReadOnlyList<uint> uids, CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            // A bare EXPUNGE purges every \Deleted message in the folder, including ones another
            // client marked and has not purged. UID EXPUNGE (UIDPLUS) limits it to ours — without
            // it, refusing beats widening the purge. Capabilities are read after authentication.
            if (!_client.Capabilities.HasFlag(ImapCapabilities.UidPlus))
                return Result.Failure("The mail server cannot delete single messages (no UIDPLUS)");

            var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var ids = uids.Select(uid => new UniqueId(uid)).ToList();
            await folder.AddFlagsAsync(ids, MessageFlags.Deleted, silent: true, cancellationToken);
            await folder.ExpungeAsync(ids, cancellationToken);

            return Result.Success();
        },
            "Unable to delete the messages",
            ex => _logger.LogError(ex, "Failed to expunge {Count} messages from {Folder}", uids.Count, folderPath),
            FolderSentinel);

    public Task<Result> EmptyAsync(string folderPath, string? targetPath, CancellationToken cancellationToken)
    {
        // Read before the operation, not inside it: the failure log below is a sibling argument
        // and cannot see the operation's locals.
        var move = !string.IsNullOrWhiteSpace(targetPath);

        return ExecuteAsync(cancellationToken, async () =>
        {
            IMailFolder? target = null;
            if (move)
            {
                var targetResult = await ResolveTargetOrFailAsync(targetPath!, cancellationToken);
                if (targetResult.IsFailure) return Result.Failure(targetResult.Error);
                target = targetResult.Value;
            }

            var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var uids = await folder.SearchAsync(SearchQuery.All, cancellationToken);
            if (uids.Count == 0) return Result.Success();

            if (move)
            {
                await folder.MoveToAsync(uids, target!, cancellationToken);
            }
            else
            {
                // Bare EXPUNGE purges every \Deleted message; emptying purges the whole folder,
                // so no UID EXPUNGE (UIDPLUS) is needed — unlike DeleteAsync which targets a subset.
                await folder.AddFlagsAsync(uids, MessageFlags.Deleted, silent: true, cancellationToken);
                await folder.ExpungeAsync(cancellationToken);
            }

            return Result.Success();
        },
            "Unable to empty the folder",
            ex => _logger.LogError(ex, "Failed to empty {Folder} (move: {Move})", folderPath, move),
            FolderSentinel);
    }

    public Task<Result> AppendAsync(string folderPath, MimeMessage message, bool seen, CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
            await folder.AppendAsync(message, seen ? MessageFlags.Seen : MessageFlags.None, cancellationToken);
            return Result.Success();
        },
            "Unable to file the message",
            ex => _logger.LogError(ex, "Failed to append a message to {Folder}", folderPath),
            FolderSentinel);

    public Task<Result<uint>> SaveDraftAsync(
        string folderPath, MimeMessage message, uint? replaceUid, CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
            var appended = await folder.AppendAsync(message, MessageFlags.Draft | MessageFlags.Seen, cancellationToken);
            // No APPENDUID (UIDPLUS absent) would leave the composer unable to replace this version
            // on its next save, piling up one copy per save: refuse outright, like DeleteAsync does.
            if (appended == null)
                return Result.Failure<uint>("The mail server cannot track saved drafts (no UIDPLUS)");

            if (replaceUid is { } previous)
            {
                try
                {
                    await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
                    var ids = new List<UniqueId> { new(previous) };
                    await folder.AddFlagsAsync(ids, MessageFlags.Deleted, silent: true, cancellationToken);
                    await folder.ExpungeAsync(ids, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // The new version is already filed; an orphan predecessor is visible but harmless
                    // and goes with the folder's next manual cleanup.
                    _logger.LogWarning(ex, "Could not remove replaced draft {Uid} in {Folder}", previous, folderPath);
                }
            }

            return Result.Success(appended.Value.Id);
        },
            "Unable to save the draft",
            ex => _logger.LogError(ex, "Failed to save a draft in {Folder}", folderPath),
            FolderSentinel);

    public Task<Result<MailSearchPage>> SearchAsync(
        string folderPath, bool allFolders, MailSearchCriteria criteria, int page, int pageSize, CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            var query = MailSearchQueryBuilder.Build(criteria, DateTime.UtcNow.Date);
            var result = new MailSearchPage { Page = page, PageSize = pageSize };

            // Every match as (folder, uid), already newest-first once this list is final.
            List<(IMailFolder Folder, UniqueId Uid)> matches;

            if (!allFolders && _client.Capabilities.HasFlag(ImapCapabilities.Sort))
            {
                // Single folder with SORT: the server hands the order, no dates needed.
                var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
                await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

                var sorted = await folder.SortAsync(query, [OrderBy.ReverseDate], cancellationToken);
                var uids = criteria.HasAttachment
                    ? await WithAttachmentsAsync(folder, BoundCandidates(sorted, page, pageSize), cancellationToken)
                    : sorted;
                matches = uids.Select(uid => (folder, uid)).ToList();
                result.Total = criteria.HasAttachment ? matches.Count : sorted.Count;
            }
            else
            {
                // All folders — or a server without SORT: per folder, SORT (REVERSE DATE) when the
                // session advertises it (rule 2), UID-descending as the newest-first proxy otherwise,
                // then one FETCH for the merge key (Envelope.Date, arrival as fallback) and — only
                // when attachments are asked for — BODYSTRUCTURE. The FETCH is bounded to the
                // (page + 1) * pageSize candidates that can still reach the requested page once the
                // folders are merged; the SEARCH itself stays whole, so the match count stays exact.
                var fetchItems = MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope
                                 | MessageSummaryItems.InternalDate;
                if (criteria.HasAttachment) fetchItems |= MessageSummaryItems.BodyStructure;

                var sortCapable = _client.Capabilities.HasFlag(ImapCapabilities.Sort);
                var exactTotal = 0;
                var hits = new List<SearchHit>();
                foreach (var folder in await SearchableFoldersAsync(folderPath, allFolders, cancellationToken))
                {
                    await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

                    IList<UniqueId> found = sortCapable
                        ? await folder.SortAsync(query, [OrderBy.ReverseDate], cancellationToken)
                        : await folder.SearchAsync(query, cancellationToken);
                    if (found.Count == 0) continue;
                    exactTotal += found.Count;

                    var candidates = BoundCandidates(
                        sortCapable ? found : found.OrderByDescending(uid => uid.Id).ToList(),
                        page, pageSize);

                    var items = await folder.FetchAsync(candidates, fetchItems, cancellationToken);
                    hits.AddRange(items.Select(item => new SearchHit(
                        item.UniqueId, folder,
                        SortKeyOf(item.Envelope?.Date, item.InternalDate),
                        item.Attachments?.Any() ?? false)));
                }

                // Filter (attachments) and sort BEFORE pagination. Under the post-filter only the
                // bounded candidates were ever examined, so Total is "at least" what is counted
                // here; without it, SEARCH already said exactly how many match.
                matches = OrderHits(hits, criteria.HasAttachment)
                    .Select(hit => (hit.Folder, hit.Uid)).ToList();
                result.Total = criteria.HasAttachment ? matches.Count : exactTotal;
            }

            var wanted = PageOf(matches, page, pageSize);
            if (wanted.Count == 0) return Result.Success(result);

            // One summary fetch per folder present in the page. Each folder is re-opened:
            // IMAP selects one mailbox at a time, so the loop above left only the last one open.
            var byKey = new Dictionary<(string, uint), MailSearchResult>();
            foreach (var group in wanted.GroupBy(m => m.Folder))
            {
                await group.Key.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
                var items = await group.Key.FetchAsync(
                    group.Select(m => m.Uid).ToList(), SummaryItems, SummaryHeaders, cancellationToken);
                foreach (var item in items)
                {
                    byKey[(group.Key.FullName, item.UniqueId.Id)] = FillSummary(new MailSearchResult
                    {
                        FolderPath = group.Key.FullName,
                        UidValidity = group.Key.UidValidity,
                    }, item);
                }
            }

            // Back into merged order; a uid expunged between search and fetch just drops out.
            foreach (var match in wanted)
            {
                if (byKey.TryGetValue((match.Folder.FullName, match.Uid.Id), out var row))
                    result.Results.Add(row);
            }

            return Result.Success(result);
        },
            "Unable to search the messages",
            ex => _logger.LogError(ex, "Failed to search messages from {Folder} (allFolders: {AllFolders})", folderPath, allFolders),
            FolderSentinel);

    /// <summary>The folders one search sweeps: the named one, or every selectable folder.</summary>
    private async Task<IReadOnlyList<IMailFolder>> SearchableFoldersAsync(
        string folderPath, bool allFolders, CancellationToken cancellationToken)
    {
        if (!allFolders) return [await _client.GetFolderAsync(folderPath, cancellationToken)];

        var folders = await _client.GetFoldersAsync(_client.PersonalNamespaces[0], cancellationToken: cancellationToken);
        return folders
            .Where(f => (f.Attributes & (FolderAttributes.NonExistent | FolderAttributes.NoSelect)) == 0)
            .ToList();
    }

    /// <summary>
    /// Keeps only the matches whose BODYSTRUCTURE shows an attachment — the same predicate
    /// that fills HasAttachments. Runs before paging: filtering after would falsify Total.
    /// </summary>
    private static async Task<IList<UniqueId>> WithAttachmentsAsync(
        IMailFolder folder, IList<UniqueId> uids, CancellationToken cancellationToken)
    {
        if (uids.Count == 0) return uids;

        var items = await folder.FetchAsync(
            uids, MessageSummaryItems.UniqueId | MessageSummaryItems.BodyStructure, cancellationToken);
        var keep = items.Where(i => i.Attachments?.Any() ?? false).Select(i => i.UniqueId).ToHashSet();
        return uids.Where(keep.Contains).ToList();
    }

    /// <summary>
    /// The per-folder candidate window: once every folder's newest-first list is merged, only its
    /// first (page + 1) * pageSize entries can still land on the requested page, so nothing past
    /// them is worth a per-message FETCH. Long arithmetic: the controller caps pageSize, not page.
    /// </summary>
    internal static IList<UniqueId> BoundCandidates(IList<UniqueId> newestFirst, int page, int pageSize)
    {
        var take = (page + 1L) * pageSize;
        if (take <= 0) return [];

        return take >= newestFirst.Count ? newestFirst : newestFirst.Take((int)take).ToList();
    }

    /// <summary>A merge-path match: folder+uid to fetch, the sent-date sort key, its attachment flag.</summary>
    internal sealed record SearchHit(UniqueId Uid, IMailFolder Folder, DateTimeOffset SortKey, bool HasAttachment);

    /// <summary>
    /// The merge sort key: the sent date, falling back to arrival then MinValue so a malformed
    /// message missing its Envelope.Date still orders sanely and never yields null.
    /// </summary>
    internal static DateTimeOffset SortKeyOf(DateTimeOffset? sentDate, DateTimeOffset? internalDate)
        => sentDate ?? internalDate ?? DateTimeOffset.MinValue;

    /// <summary>
    /// Orders merge-path hits by sent date, newest first, optionally keeping only those carrying an
    /// attachment. Pure so the refine-before-Total ordering is unit-tested apart from any IMAP call.
    /// </summary>
    internal static List<SearchHit> OrderHits(IEnumerable<SearchHit> hits, bool attachmentsOnly)
    {
        var kept = attachmentsOnly ? hits.Where(hit => hit.HasAttachment) : hits;
        return kept.OrderByDescending(hit => hit.SortKey).ToList();
    }

    public Task<Result<MailFolderStatus>> GetFolderStatusAsync(string path, CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await _client.GetFolderAsync(path, cancellationToken);

            var selectable = (folder.Attributes & FolderAttributes.NonExistent) == 0
                             && (folder.Attributes & FolderAttributes.NoSelect) == 0;

            // STATUS on a \NoSelect folder is a protocol error; the caller rejects the
            // folder on Selectable alone, so there is nothing more to read.
            if (!selectable)
            {
                return Result.Success(new MailFolderStatus { Path = folder.FullName, Selectable = false });
            }

            var items = StatusItems.UidValidity;
            if (_client.Capabilities.HasFlag(ImapCapabilities.ObjectID))
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
            ex => _logger.LogError(ex, "Failed to read the status of {Folder}", path),
            FolderSentinel);

    public Task<Result<MailFolderPage>> ListMessagesAsync(string folderPath, int page, int pageSize, CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var result = new MailFolderPage
            {
                FolderPath = folder.FullName,
                UidValidity = folder.UidValidity,
                Total = folder.Count,
                Page = page,
                PageSize = pageSize
            };

            // SORT asks the server for date order. Without it the page is a window on the
            // sequence numbers, which is arrival-into-the-folder order — the same thing in an
            // inbox, but not in a folder messages are *moved* to: a trash lists by when each
            // message was thrown away, not by its date.
            if (_client.Capabilities.HasFlag(ImapCapabilities.Sort))
            {
                var sorted = await folder.SortAsync(
                    SearchQuery.All, [OrderBy.ReverseDate], cancellationToken);

                var wanted = PageOf(sorted, page, pageSize).ToList();
                if (wanted.Count == 0) return Result.Success(result);

                var sortedItems = await folder.FetchAsync(wanted, SummaryItems, SummaryHeaders, cancellationToken);
                foreach (var item in InOrderOf(sortedItems, wanted, item => item.UniqueId))
                {
                    result.Messages.Add(ToSummary(item));
                }

                return Result.Success(result);
            }

            var (start, end) = ComputePageWindow(folder.Count, page, pageSize);
            if (start < 0) return Result.Success(result);

            var items = await folder.FetchAsync(start, end, SummaryItems, SummaryHeaders, cancellationToken);

            // The fetch runs oldest-first; the list is newest-first.
            foreach (var item in items.Reverse())
            {
                result.Messages.Add(ToSummary(item));
            }

            return Result.Success(result);
        },
            "Unable to read the messages",
            ex => _logger.LogError(ex, "Failed to list messages in {Folder}", folderPath),
            FolderSentinel);

    private const MessageSummaryItems SummaryItems =
        MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
        MessageSummaryItems.Size | MessageSummaryItems.BodyStructure | MessageSummaryItems.InternalDate |
        MessageSummaryItems.PreviewText;

    /// <summary>
    /// Priority is not in the envelope, so the summary FETCH has to name its headers. On the wire
    /// this is one BODY.PEEK[HEADER.FIELDS (...)] alongside the items above — one more item in the
    /// same round trip, not a second request, and the price of showing priority in the list at all.
    /// </summary>
    private static readonly string[] SummaryHeaders = MailPriorityReader.Fields;

    private static MailMessageSummary ToSummary(IMessageSummary item) => FillSummary(new MailMessageSummary(), item);

    /// <summary>One mapping for list rows and search hits — the fields cannot drift apart.</summary>
    internal static T FillSummary<T>(T summary, IMessageSummary item) where T : MailMessageSummary
    {
        var sender = item.Envelope?.From?.Mailboxes?.FirstOrDefault();

        summary.Uid = item.UniqueId.Id;
        summary.Subject = item.Envelope?.Subject ?? string.Empty;
        summary.FromName = sender?.Name is { Length: > 0 } name ? name : sender?.Address ?? string.Empty;
        summary.FromAddress = sender?.Address ?? string.Empty;
        summary.To = ToAddressInfos(item.Envelope?.To);
        // Arrival date, not the Date header. The page window is a range of sequence
        // numbers, so the list is ordered by arrival; showing the header date would
        // print a date that contradicts the row's own position — a message written in
        // May but delivered in June sits among the June messages, and saying "May"
        // there reads as a sorting bug. The header date is still shown in the reader,
        // where it answers a different question: when the sender wrote it.
        summary.Date = item.InternalDate ?? item.Envelope?.Date ?? DateTimeOffset.MinValue;
        summary.Seen = item.Flags?.HasFlag(MessageFlags.Seen) ?? false;
        summary.Flagged = item.Flags?.HasFlag(MessageFlags.Flagged) ?? false;
        summary.Answered = item.Flags?.HasFlag(MessageFlags.Answered) ?? false;
        summary.HasAttachments = item.Attachments?.Any() ?? false;
        summary.Size = item.Size ?? 0;
        summary.Preview = item.PreviewText ?? string.Empty;
        summary.Priority = item.Headers is { } headers ? MailPriorityReader.Parse(headers) : MailPriority.Normal;
        return summary;
    }

    public static List<MailAddressInfo> ToAddressInfos(InternetAddressList? addresses) =>
        addresses?.Mailboxes?.Select(m => new MailAddressInfo(m.Name ?? string.Empty, m.Address)).ToList() ?? [];

    /// <summary>Threading and reply-routing headers — 2c2b's transcription duty on the detail.</summary>
    internal static void ApplyThreading(MailMessageDetail detail, MimeMessage message)
    {
        detail.MessageId = string.IsNullOrWhiteSpace(message.MessageId) ? null : message.MessageId;
        detail.References = message.References?.ToList() ?? [];
        detail.InReplyTo = string.IsNullOrWhiteSpace(message.InReplyTo) ? null : message.InReplyTo;
        detail.ReplyTo = ToAddressInfos(message.ReplyTo);
        detail.Bcc = ToAddressInfos(message.Bcc);
    }

    // Servers report Content-ID with or without <>; the HTML's cid: references are always bare.
    internal static string? TrimAngleBrackets(string? contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId)) return null;
        var trimmed = contentId.Trim();
        if (trimmed.StartsWith('<') && trimmed.EndsWith('>')) trimmed = trimmed[1..^1];
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>
    /// Whether a body part belongs on the message's part list. Being an attachment or carrying a
    /// file name is not enough to ask for: a logo embedded as <c>Content-Disposition: inline</c>
    /// with no file name — how Vaultwarden and others ship theirs — is neither, and dropping it
    /// left the reader with a <c>cid:</c> it had nothing to resolve against. A Content-ID is
    /// exactly the marker that the body means to display the part, so it earns a place too. What
    /// remains excluded is the message's own text and html, which carry none of the three.
    /// </summary>
    internal static bool IsListedPart(BodyPartBasic part) =>
        part.IsAttachment
        || !string.IsNullOrEmpty(part.FileName)
        || !string.IsNullOrEmpty(TrimAngleBrackets(part.ContentId));

    public Task<Result<MailMessageDetail>> GetMessageAsync(string folderPath, uint uid, CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var uniqueId = new UniqueId(folder.UidValidity, uid);

            var summaries = await folder.FetchAsync(
                new[] { uniqueId },
                MessageSummaryItems.UniqueId | MessageSummaryItems.BodyStructure,
                cancellationToken);

            var summary = summaries.FirstOrDefault();
            if (summary == null) return Result.Failure<MailMessageDetail>(MessageNotFound);

            // Never BODY.PEEK[] here: the whole-message fetch pulled every attachment over the
            // wire to render a few KB of body. BODYSTRUCTURE names the text parts, so only those
            // and the header block are fetched; attachments stay on the server until clicked.
            var headers = await folder.GetHeadersAsync(uniqueId, cancellationToken);
            var message = ToHeaderOnlyMessage(headers);

            var htmlBody = summary.HtmlBody is { } htmlPart
                ? await ReadTextPartAsync(folder, uniqueId, htmlPart, cancellationToken)
                : null;
            var textBody = summary.TextBody is { } textPart
                ? await ReadTextPartAsync(folder, uniqueId, textPart, cancellationToken)
                : null;

            var sanitized = _sanitizer.Sanitize(htmlBody ?? string.Empty);
            var sender = message.From?.Mailboxes?.FirstOrDefault();
            var headerDetails = MailHeaderDetailsReader.Parse(message.Headers);

            var detail = new MailMessageDetail
            {
                Uid = uid,
                FolderPath = folder.FullName,
                UidValidity = folder.UidValidity,
                Subject = message.Subject ?? string.Empty,
                FromName = sender?.Name is { Length: > 0 } name ? name : sender?.Address ?? string.Empty,
                FromAddress = sender?.Address ?? string.Empty,
                To = ToAddressInfos(message.To),
                Cc = ToAddressInfos(message.Cc),
                Date = message.Date,
                HtmlBody = sanitized.Html,
                TextBody = textBody ?? string.Empty,
                BlockedImageCount = sanitized.BlockedImageCount,
                Truncated = sanitized.Truncated,
                Authentication = MailAuthenticationReader.Parse(message.Headers),
                SpamScore = MailSpamScoreReader.Parse(message.Headers),
                MailingList = headerDetails.MailingList,
                SentBy = headerDetails.SentBy,
                SignedBy = headerDetails.SignedBy,
                UnsubscribeUrl = headerDetails.UnsubscribeUrl,
                TlsReceived = headerDetails.TlsReceived,
                Priority = MailPriorityReader.Parse(message.Headers)
            };

            ApplyThreading(detail, message);

            foreach (var part in summary.BodyParts.OfType<BodyPartBasic>())
            {
                if (!IsListedPart(part)) continue;

                detail.Attachments.Add(new MailAttachmentInfo
                {
                    Part = part.PartSpecifier,
                    FileName = string.IsNullOrEmpty(part.FileName) ? "attachment" : part.FileName,
                    ContentType = part.ContentType?.MimeType ?? "application/octet-stream",
                    Size = part.Octets,
                    IsInline = !part.IsAttachment,
                    ContentId = TrimAngleBrackets(part.ContentId)
                });
            }

            return Result.Success(detail);
        },
            "Unable to read the message",
            ex => _logger.LogError(ex, "Failed to read message {Uid} in {Folder}", uid, folderPath),
            FolderOrMessageSentinel);

    /// <summary>
    /// Rehydrates a fetched header block into a MimeMessage, so every header-derived field —
    /// envelope, threading (In-Reply-To/References), the four readers — keeps MimeKit's exact
    /// decoding, byte-identical to when the whole message was downloaded and parsed.
    /// </summary>
    internal static MimeMessage ToHeaderOnlyMessage(HeaderList headers)
    {
        using var buffer = new MemoryStream();
        headers.WriteTo(buffer);
        buffer.Position = 0;
        return MimeMessage.Load(buffer);
    }

    /// <summary>
    /// IMAP spells "the body of a non-multipart message" TEXT, not an empty section — an empty
    /// section is the whole RFC822 message, headers included.
    /// </summary>
    internal static string SectionOf(BodyPartBasic part) =>
        part.PartSpecifier.Length == 0 ? "TEXT" : part.PartSpecifier;

    /// <summary>
    /// Fetches one text part by its specifier and decodes it the way MimeKit decodes a parsed
    /// message's body — same transfer decoding, same charset handling and fallbacks — so a
    /// quoted-printable accented body reads exactly as it did off the whole-message fetch.
    /// </summary>
    private static async Task<string> ReadTextPartAsync(
        IMailFolder folder, UniqueId uniqueId, BodyPartText part, CancellationToken cancellationToken)
    {
        using var encoded = await folder.GetStreamAsync(uniqueId, SectionOf(part), cancellationToken);
        MimeUtils.TryParse(part.ContentTransferEncoding, out ContentEncoding encoding);

        var textPart = new TextPart(part.ContentType.MediaSubtype)
        {
            Content = new MimeContent(encoded, encoding)
        };
        foreach (var parameter in part.ContentType.Parameters)
            textPart.ContentType.Parameters[parameter.Name] = parameter.Value;

        return textPart.Text;
    }

    /// <summary>The message as MimeKit parsed it — PrepareQuote needs the raw body and its parts.</summary>
    public Task<Result<MimeMessage>> GetMimeMessageAsync(string folderPath, uint uid, CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
            var message = await folder.GetMessageAsync(new UniqueId(folder.UidValidity, uid), cancellationToken);
            return Result.Success(message);
        },
            "Unable to read the message",
            ex => _logger.LogError(ex, "Failed to read raw message {Uid} in {Folder}", uid, folderPath),
            MessageSentinel);

    /// <summary>
    /// Two IMAP round trips: an envelope-plus-size fetch that also pulls the
    /// Authentication-Results header, then a BODY[]&lt;0.N&gt; partial fetch for the bytes.
    /// </summary>
    public Task<Result<MailMessageSource>> GetMessageSourceAsync(
        string folderPath, uint uid, int maxBytes, CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var uniqueId = new UniqueId(folder.UidValidity, uid);

            var summaries = await folder.FetchAsync(
                new[] { uniqueId },
                MessageSummaryItems.Envelope | MessageSummaryItems.Size,
                new[] { HeaderId.AuthenticationResults },
                cancellationToken);

            var summary = summaries.FirstOrDefault();
            if (summary?.Envelope == null) return Result.Failure<MailMessageSource>(MessageNotFound);

            using var stream = await folder.GetStreamAsync(uniqueId, 0, maxBytes, cancellationToken);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();

            // UTF-8 with .NET's replacing decoder: headers are ASCII either way, and a modern
            // 8-bit body is UTF-8 far more often than it is anything else. A sequence cut in
            // half by the cap costs one replacement character at the very tail.
            var source = Encoding.UTF8.GetString(bytes);

            var envelope = summary.Envelope;
            var from = envelope.From.Mailboxes.FirstOrDefault();

            // bytes.LongLength can never exceed maxBytes (the stream itself is capped there), so
            // when the server omits RFC822.SIZE there is no ground truth to compare against and
            // IsTruncated's ">" would always read false. Erring toward "there may be more": a
            // full-cap read is treated as truncated rather than trusted as the whole message.
            long total;
            bool truncated;
            if (summary.Size.HasValue)
            {
                total = (long)summary.Size.Value;
                truncated = MailMessageSource.IsTruncated(total, maxBytes);
            }
            else
            {
                total = bytes.LongLength;
                truncated = bytes.LongLength >= maxBytes;
            }

            return Result.Success(new MailMessageSource(
                Subject: envelope.Subject ?? string.Empty,
                MessageId: TrimAngleBrackets(envelope.MessageId),
                Date: envelope.Date ?? DateTimeOffset.MinValue,
                FromName: from?.Name ?? string.Empty,
                FromAddress: from?.Address ?? string.Empty,
                To: ToAddressInfos(envelope.To),
                Authentication: MailAuthenticationReader.Parse(summary.Headers ?? []),
                Source: source,
                TotalBytes: total,
                Truncated: truncated));
        },
            "Unable to read the message source",
            ex => _logger.LogError(ex, "Failed to read the source of {Uid} in {Folder}", uid, folderPath),
            MessageSentinel);

    public Task<Result<MailAttachmentContent>> GetAttachmentAsync(string folderPath, uint uid, string partSpecifier, CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var uniqueId = new UniqueId(folder.UidValidity, uid);

            var summaries = await folder.FetchAsync(new[] { uniqueId }, MessageSummaryItems.BodyStructure, cancellationToken);
            var summary = summaries.FirstOrDefault();
            if (summary == null) return Result.Failure<MailAttachmentContent>(MessageNotFound);

            var part = summary.BodyParts.OfType<BodyPartBasic>()
                .FirstOrDefault(p => string.Equals(p.PartSpecifier, partSpecifier, StringComparison.Ordinal));
            if (part == null) return Result.Failure<MailAttachmentContent>(AttachmentNotFound);

            var entity = await folder.GetBodyPartAsync(uniqueId, part, cancellationToken);
            if (entity is not MimePart mimePart) return Result.Failure<MailAttachmentContent>(AttachmentNotFound);

            // A part with no content is not an attachment we can serve, decoded or otherwise.
            if (mimePart.Content == null) return Result.Failure<MailAttachmentContent>(AttachmentNotFound);

            // GetBodyPartAsync has already materialised the part, so this decodes an in-memory
            // entity rather than the socket — MailKit exposes no true socket-to-response path.
            // What it does avoid is the ToArray() that used to follow: the buffer is handed over
            // as-is instead of being copied a second time into a byte[] on the large object heap.
            var buffer = new MemoryStream();
            await mimePart.Content.DecodeToAsync(buffer, cancellationToken);
            buffer.Position = 0;

            return Result.Success(new MailAttachmentContent
            {
                Content = buffer,
                FileName = string.IsNullOrEmpty(part.FileName) ? "attachment" : part.FileName,
                ContentType = part.ContentType?.MimeType ?? "application/octet-stream"
            });
        },
            "Unable to read the attachment",
            ex => _logger.LogError(ex, "Failed to read attachment {Part} of message {Uid}", partSpecifier, uid),
            FolderOrMessageSentinel);

    /// <summary>
    /// Sentinel errors the controller maps to 404 rather than 502. Shared constants so the
    /// two layers cannot drift apart on the exact wording.
    /// </summary>
    public const string MessageNotFound = "Message not found";
    public const string AttachmentNotFound = "Attachment not found";
    public const string FolderNotFound = "Folder not found";

    private static bool IsInbox(IMailFolder folder)
        => string.Equals(folder.FullName, "INBOX", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parent path, or null when the folder sits at the namespace root.</summary>
    public static string? ParentPath(string fullName, char separator)
    {
        var index = fullName.LastIndexOf(separator);
        return index <= 0 ? null : fullName[..index];
    }

    /// <summary>
    /// A leaf name may not contain the hierarchy separator: it would silently create a
    /// nested folder instead of the one the user asked for.
    /// </summary>
    public static bool IsValidLeafName(string name, char separator)
        => !string.IsNullOrWhiteSpace(name) && !name.Contains(separator);

    public static string CombinePath(string parentPath, string name, char separator)
        => string.IsNullOrEmpty(parentPath) ? name : $"{parentPath}{separator}{name}";

    /// <summary>
    /// Maps a newest-first page onto an IMAP sequence range, which runs oldest-first: page
    /// zero is the window at the *end* of the folder. Returns (-1, -1) when the page lies
    /// past the end, or when the arguments make no sense.
    /// </summary>
    public static (int Start, int End) ComputePageWindow(int total, int page, int pageSize)
    {
        if (total <= 0 || page < 0 || pageSize <= 0) return (-1, -1);

        var end = total - 1 - (page * pageSize);
        if (end < 0) return (-1, -1);

        var start = Math.Max(0, end - pageSize + 1);
        return (start, end);
    }

    /// <summary>Slice of an already-ordered list, or empty when the page lies past its end.</summary>
    public static IReadOnlyList<T> PageOf<T>(IEnumerable<T> ordered, int page, int pageSize)
    {
        if (page < 0 || pageSize <= 0) return [];

        return ordered.Skip(page * pageSize).Take(pageSize).ToList();
    }

    /// <summary>
    /// Re-orders fetched items to match the order they were asked for. A server answers a UID
    /// FETCH in whatever order it likes — ascending UID in practice, which is exactly not the
    /// sort order requested.
    /// </summary>
    public static IReadOnlyList<T> InOrderOf<T, TKey>(
        IEnumerable<T> items, IEnumerable<TKey> order, Func<T, TKey> keyOf) where TKey : notnull
    {
        var byKey = items.GroupBy(keyOf).ToDictionary(group => group.Key, group => group.First());

        return order.Where(byKey.ContainsKey).Select(key => byKey[key]).ToList();
    }

    /// <summary>
    /// Assigns discovered roles, each to at most one folder — and each folder to at most
    /// one role.
    /// </summary>
    /// <remarks>
    /// Two claim sets, not one. Claimed roles keep a mailbox holding both "Drafts" and
    /// "Brouillons" from ending up with two drafts folders. Claimed folders keep one
    /// folder from holding two roles — a folder flagged \Sent but named "Trash" used to
    /// claim both, which is undecidable to display. Callers may seed both sets: the role
    /// resolver runs user overrides first and hands discovery only the leftovers.
    ///
    /// A non-selectable folder never holds a role, in either pass. The ordinary shape
    /// that makes this load-bearing: "Archive" exists only as a \NoSelect container for
    /// "Archive/2024" and "Archive/2025". Letting the container win the name pass stamped
    /// a role on a mailbox that cannot hold a message and locked the real archive folder
    /// out of it. Level 1 already refuses a non-selectable override target; the same rule
    /// has to hold here.
    /// </remarks>
    public static IReadOnlyDictionary<string, SpecialUseAssignment> ResolveSpecialUses(
        IEnumerable<(string Path, string Name, string? AttributeRole, bool Selectable)> folders,
        IEnumerable<string>? claimedRoles = null,
        IEnumerable<string>? claimedFolders = null)
    {
        var candidates = folders.ToList();
        var roles = new HashSet<string>(claimedRoles ?? [], StringComparer.Ordinal);
        var taken = new HashSet<string>(claimedFolders ?? [], StringComparer.Ordinal);
        var result = new Dictionary<string, SpecialUseAssignment>(StringComparer.Ordinal);

        foreach (var folder in candidates)
        {
            if (!folder.Selectable) continue;
            if (folder.AttributeRole is not { } role) continue;
            if (taken.Contains(folder.Path)) continue;

            if (!roles.Contains(role))
            {
                roles.Add(role);
                result[folder.Path] = new SpecialUseAssignment(role, SpecialUseAssignment.FromFlag);
            }

            // Taken whether it won the role or lost it. A folder the server flagged is
            // never a candidate for name guessing: losing the race to another \Sent folder
            // must leave it with no role at all, because showing it as "Trash" on the
            // strength of its name would let a guess contradict what the server declared.
            taken.Add(folder.Path);
        }

        foreach (var folder in candidates)
        {
            if (!folder.Selectable) continue;

            if (SpecialUseFromName(folder.Name) is { } role && !roles.Contains(role) && !taken.Contains(folder.Path))
            {
                roles.Add(role);
                taken.Add(folder.Path);
                result[folder.Path] = new SpecialUseAssignment(role, SpecialUseAssignment.FromName);
            }
        }

        return result;
    }

    public static string? SpecialUseFromAttributes(FolderAttributes attributes, bool isInbox)
    {
        if (isInbox) return "inbox";

        if ((attributes & FolderAttributes.Sent) != 0) return "sent";
        if ((attributes & FolderAttributes.Drafts) != 0) return "drafts";
        if ((attributes & FolderAttributes.Trash) != 0) return "trash";
        if ((attributes & FolderAttributes.Junk) != 0) return "junk";
        if ((attributes & FolderAttributes.Archive) != 0) return "archive";

        return null;
    }

    /// <summary>
    /// Last-resort guess for servers that advertise no SPECIAL-USE. Covers the localised
    /// names a mail client creates when it, not the server, provisioned the folders.
    /// </summary>
    public static string? SpecialUseFromName(string name) => name.ToLowerInvariant() switch
    {
        "inbox" => "inbox",
        "sent" or "sent messages" or "sent items"
            or "envoyés" or "éléments envoyés" or "messages envoyés" => "sent",
        "drafts" or "draft" or "brouillons" => "drafts",
        "trash" or "deleted" or "deleted messages" or "deleted items"
            or "corbeille" or "éléments supprimés" => "trash",
        "junk" or "spam" or "junk e-mail"
            or "courrier indésirable" or "indésirables" or "pourriel" => "junk",
        "archive" or "archives" => "archive",
        _ => null
    };

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ImapSession));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync(quit: true);
            }
        }
        catch
        {
            // Best effort — the connection is being torn down anyway.
        }

        _client.Dispose();
    }
}
