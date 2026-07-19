using CSharpFunctionalExtensions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;
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

    public async Task<Result<IReadOnlyList<MailFolderNode>>> ListFoldersAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        try
        {
            var personal = _client.PersonalNamespaces[0];

            // Asking for a STATUS item the server never advertised is a protocol error, so
            // the capability gates the request itself, not just the reading of the result.
            var statusItems = StatusItems.Count | StatusItems.Unread | StatusItems.UidValidity;
            if (_client.Capabilities.HasFlag(ImapCapabilities.ObjectID))
                statusItems |= StatusItems.MailboxId;

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
                    UidValidity = folder.UidValidity
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
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list IMAP folders");
            return Result.Failure<IReadOnlyList<MailFolderNode>>("Unable to read the mailbox folders");
        }
    }

    public async Task<Result<string>> CreateFolderAsync(string parentPath, string name, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (!IsValidLeafName(name, DirectorySeparator))
        {
            return Result.Failure<string>($"A folder name cannot be empty or contain '{DirectorySeparator}'");
        }

        try
        {
            var parent = string.IsNullOrEmpty(parentPath)
                ? _client.GetFolder(_client.PersonalNamespaces[0])
                : await _client.GetFolderAsync(parentPath, cancellationToken);

            var created = await parent.CreateAsync(name, isMessageFolder: true, cancellationToken);

            // A folder the user just created should show up without a second step.
            await created.SubscribeAsync(cancellationToken);

            return Result.Success(created.FullName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create folder {Name} under {Parent}", name, parentPath);
            return Result.Failure<string>("Unable to create the folder");
        }
    }

    public async Task<Result<string>> RenameFolderAsync(string path, string newParentPath, string newName, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (!IsValidLeafName(newName, DirectorySeparator))
        {
            return Result.Failure<string>($"A folder name cannot be empty or contain '{DirectorySeparator}'");
        }

        try
        {
            var folder = await _client.GetFolderAsync(path, cancellationToken);
            var newParent = string.IsNullOrEmpty(newParentPath)
                ? _client.GetFolder(_client.PersonalNamespaces[0])
                : await _client.GetFolderAsync(newParentPath, cancellationToken);

            await folder.RenameAsync(newParent, newName, cancellationToken);

            return Result.Success(folder.FullName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename folder {Path}", path);
            return Result.Failure<string>("Unable to rename the folder");
        }
    }

    public async Task<Result> DeleteFolderAsync(string path, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        try
        {
            var folder = await _client.GetFolderAsync(path, cancellationToken);

            if ((folder.Attributes & FolderAttributes.Inbox) != 0)
            {
                return Result.Failure("The inbox cannot be deleted");
            }

            await folder.DeleteAsync(cancellationToken);

            return Result.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete folder {Path}", path);
            return Result.Failure("Unable to delete the folder");
        }
    }

    public async Task<Result> SetSubscriptionAsync(string path, bool subscribed, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        try
        {
            var folder = await _client.GetFolderAsync(path, cancellationToken);

            if (subscribed) await folder.SubscribeAsync(cancellationToken);
            else await folder.UnsubscribeAsync(cancellationToken);

            return Result.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set subscription on {Path}", path);
            return Result.Failure("Unable to change the folder visibility");
        }
    }

    public async Task<Result<MailFolderStatus>> GetFolderStatusAsync(string path, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        try
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
        }
        catch (FolderNotFoundException)
        {
            return Result.Failure<MailFolderStatus>(FolderNotFound);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read the status of {Folder}", path);
            return Result.Failure<MailFolderStatus>("Unable to read the folder");
        }
    }

    public async Task<Result<MailFolderPage>> ListMessagesAsync(string folderPath, int page, int pageSize, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        try
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

                var sortedItems = await folder.FetchAsync(wanted, SummaryItems, cancellationToken);
                foreach (var item in InOrderOf(sortedItems, wanted, item => item.UniqueId))
                {
                    result.Messages.Add(ToSummary(item));
                }

                return Result.Success(result);
            }

            var (start, end) = ComputePageWindow(folder.Count, page, pageSize);
            if (start < 0) return Result.Success(result);

            var items = await folder.FetchAsync(start, end, SummaryItems, cancellationToken);

            // The fetch runs oldest-first; the list is newest-first.
            foreach (var item in items.Reverse())
            {
                result.Messages.Add(ToSummary(item));
            }

            return Result.Success(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list messages in {Folder}", folderPath);
            return Result.Failure<MailFolderPage>("Unable to read the messages");
        }
    }

    private const MessageSummaryItems SummaryItems =
        MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
        MessageSummaryItems.Size | MessageSummaryItems.BodyStructure | MessageSummaryItems.InternalDate |
        MessageSummaryItems.PreviewText;

    private static MailMessageSummary ToSummary(IMessageSummary item)
    {
        var sender = item.Envelope?.From?.Mailboxes?.FirstOrDefault();

        return new MailMessageSummary
        {
            Uid = item.UniqueId.Id,
            Subject = item.Envelope?.Subject ?? string.Empty,
            FromName = sender?.Name is { Length: > 0 } name ? name : sender?.Address ?? string.Empty,
            FromAddress = sender?.Address ?? string.Empty,
            // Arrival date, not the Date header. The page window is a range of sequence
            // numbers, so the list is ordered by arrival; showing the header date would
            // print a date that contradicts the row's own position — a message written in
            // May but delivered in June sits among the June messages, and saying "May"
            // there reads as a sorting bug. The header date is still shown in the reader,
            // where it answers a different question: when the sender wrote it.
            Date = item.InternalDate ?? item.Envelope?.Date ?? DateTimeOffset.MinValue,
            Seen = item.Flags?.HasFlag(MessageFlags.Seen) ?? false,
            Flagged = item.Flags?.HasFlag(MessageFlags.Flagged) ?? false,
            Answered = item.Flags?.HasFlag(MessageFlags.Answered) ?? false,
            HasAttachments = item.Attachments?.Any() ?? false,
            Size = item.Size ?? 0,
            Preview = item.PreviewText ?? string.Empty
        };
    }

    public async Task<Result<MailMessageDetail>> GetMessageAsync(string folderPath, uint uid, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        try
        {
            var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var uniqueId = new UniqueId(folder.UidValidity, uid);

            var summaries = await folder.FetchAsync(
                new[] { uniqueId },
                MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.BodyStructure,
                cancellationToken);

            var summary = summaries.FirstOrDefault();
            if (summary == null) return Result.Failure<MailMessageDetail>(MessageNotFound);

            var message = await folder.GetMessageAsync(uniqueId, cancellationToken);
            var sanitized = _sanitizer.Sanitize(message.HtmlBody ?? string.Empty);
            var sender = message.From?.Mailboxes?.FirstOrDefault();

            var detail = new MailMessageDetail
            {
                Uid = uid,
                FolderPath = folder.FullName,
                UidValidity = folder.UidValidity,
                Subject = message.Subject ?? string.Empty,
                FromName = sender?.Name is { Length: > 0 } name ? name : sender?.Address ?? string.Empty,
                FromAddress = sender?.Address ?? string.Empty,
                To = message.To?.Mailboxes?.Select(m => m.Address).ToList() ?? new List<string>(),
                Cc = message.Cc?.Mailboxes?.Select(m => m.Address).ToList() ?? new List<string>(),
                Date = message.Date,
                HtmlBody = sanitized.Html,
                TextBody = message.TextBody ?? string.Empty,
                BlockedImageCount = sanitized.BlockedImageCount
            };

            foreach (var part in summary.BodyParts.OfType<BodyPartBasic>())
            {
                if (!part.IsAttachment && string.IsNullOrEmpty(part.FileName)) continue;

                detail.Attachments.Add(new MailAttachmentInfo
                {
                    Part = part.PartSpecifier,
                    FileName = string.IsNullOrEmpty(part.FileName) ? "attachment" : part.FileName,
                    ContentType = part.ContentType?.MimeType ?? "application/octet-stream",
                    Size = part.Octets,
                    IsInline = !part.IsAttachment
                });
            }

            return Result.Success(detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read message {Uid} in {Folder}", uid, folderPath);
            return Result.Failure<MailMessageDetail>("Unable to read the message");
        }
    }

    public async Task<Result<MailAttachmentContent>> GetAttachmentAsync(string folderPath, uint uid, string partSpecifier, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        try
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

            using var buffer = new MemoryStream();
            await mimePart.Content.DecodeToAsync(buffer, cancellationToken);

            return Result.Success(new MailAttachmentContent
            {
                Content = buffer.ToArray(),
                FileName = string.IsNullOrEmpty(part.FileName) ? "attachment" : part.FileName,
                ContentType = part.ContentType?.MimeType ?? "application/octet-stream"
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read attachment {Part} of message {Uid}", partSpecifier, uid);
            return Result.Failure<MailAttachmentContent>("Unable to read the attachment");
        }
    }

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
