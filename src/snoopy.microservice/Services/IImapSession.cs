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
    }
}
