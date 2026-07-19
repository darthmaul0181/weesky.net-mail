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

        private static bool IsInbox(IMailFolder folder)
            => string.Equals(folder.FullName, "INBOX", StringComparison.OrdinalIgnoreCase);

        /// <summary>Parent path, or null when the folder sits at the namespace root.</summary>
        public static string? ParentPath(string fullName, char separator)
        {
            var index = fullName.LastIndexOf(separator);
            return index <= 0 ? null : fullName[..index];
        }

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
