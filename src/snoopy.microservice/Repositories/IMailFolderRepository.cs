using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Repositories
{
    public interface IMailFolderRepository
    {
        /// <summary>The user's full folder tree, subscribed and unsubscribed alike.</summary>
        Task<Result<IReadOnlyList<MailFolderNode>>> GetTreeAsync(User user, string password, CancellationToken cancellationToken);
    }
}
