using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Repositories;

public interface IMailFolderRepository
{
    /// <summary>The account's full folder tree, subscribed and unsubscribed alike.</summary>
    Task<Result<IReadOnlyList<MailFolderNode>>> GetTreeAsync(User user, MailAccountConnection connection, CancellationToken cancellationToken);

    /// <summary>Creates a folder and subscribes it. Returns its full path.</summary>
    Task<Result<string>> CreateFolderAsync(User user, MailAccountConnection connection, string parentPath, string name, CancellationToken cancellationToken);

    /// <summary>Renames a folder, optionally moving it under a different parent.</summary>
    Task<Result<string>> RenameFolderAsync(User user, MailAccountConnection connection, string path, string newParentPath, string newName, CancellationToken cancellationToken);

    /// <summary>Deletes a folder.</summary>
    Task<Result> DeleteFolderAsync(User user, MailAccountConnection connection, string path, CancellationToken cancellationToken);

    /// <summary>Subscribes or unsubscribes a folder.</summary>
    Task<Result> SetSubscriptionAsync(User user, MailAccountConnection connection, string path, bool subscribed, CancellationToken cancellationToken);

    /// <summary>Live identity of one folder — used by the role PUT to validate and capture.</summary>
    Task<Result<MailFolderStatus>> GetFolderStatusAsync(User user, MailAccountConnection connection, string path, CancellationToken cancellationToken);
}
