using CSharpFunctionalExtensions;
using MailKit;
using MailKit.Net.Imap;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services
{
    public sealed class ImapSession : IImapSession
    {
        private readonly ImapClient _client;
        private readonly ILogger _logger;
        private bool _disposed;

        public ImapSession(ImapClient client, ILogger logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
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
                var folders = await _client.GetFoldersAsync(
                    personal,
                    StatusItems.Count | StatusItems.Unread | StatusItems.UidValidity,
                    subscribedOnly: false,
                    cancellationToken);

                var nodes = new Dictionary<string, MailFolderNode>(StringComparer.Ordinal);
                var roots = new List<MailFolderNode>();

                // Ordinal sort puts a parent before its children, so the lookup below always
                // finds the parent already built.
                foreach (var folder in folders.OrderBy(f => f.FullName, StringComparer.Ordinal))
                {
                    var selectable = (folder.Attributes & FolderAttributes.NonExistent) == 0
                                     && (folder.Attributes & FolderAttributes.NoSelect) == 0;

                    var node = new MailFolderNode
                    {
                        Path = folder.FullName,
                        Name = folder.Name,
                        SpecialUse = ResolveSpecialUse(folder.Attributes, folder.Name, IsInbox(folder)),
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
        /// Maps a folder to a well-known role. The server's SPECIAL-USE flag wins; when the
        /// server advertises none, fall back to matching well-known names, which is the only
        /// option on servers without the extension.
        /// </summary>
        public static string? ResolveSpecialUse(FolderAttributes attributes, string name, bool isInbox)
        {
            if (isInbox) return "inbox";

            if ((attributes & FolderAttributes.Sent) != 0) return "sent";
            if ((attributes & FolderAttributes.Drafts) != 0) return "drafts";
            if ((attributes & FolderAttributes.Trash) != 0) return "trash";
            if ((attributes & FolderAttributes.Junk) != 0) return "junk";
            if ((attributes & FolderAttributes.Archive) != 0) return "archive";

            return name.ToLowerInvariant() switch
            {
                "inbox" => "inbox",
                "sent" or "sent messages" or "sent items" => "sent",
                "drafts" or "draft" => "drafts",
                "trash" or "deleted" or "deleted messages" or "deleted items" => "trash",
                "junk" or "spam" or "junk e-mail" => "junk",
                "archive" or "archives" => "archive",
                _ => null
            };
        }

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
}
