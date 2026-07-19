using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Repositories
{
    public interface IMailMessageRepository
    {
        /// <summary>One page of a folder, newest message first.</summary>
        Task<Result<MailFolderPage>> ListAsync(User user, string password, string folderPath, int page, int pageSize, CancellationToken cancellationToken);
    }
}
