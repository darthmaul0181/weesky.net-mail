using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services
{
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
    }
}
