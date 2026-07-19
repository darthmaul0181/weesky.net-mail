using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Repositories
{
    public interface IMailMessageRepository
    {
        /// <summary>One page of a folder, newest message first.</summary>
        Task<Result<MailFolderPage>> ListAsync(User user, string password, string folderPath, int page, int pageSize, CancellationToken cancellationToken);

        /// <summary>A single message, body sanitised and attachments listed.</summary>
        Task<Result<MailMessageDetail>> GetAsync(User user, string password, string folderPath, uint uid, CancellationToken cancellationToken);

        /// <summary>One decoded attachment, addressed by MIME part specifier.</summary>
        Task<Result<MailAttachmentContent>> GetAttachmentAsync(User user, string password, string folderPath, uint uid, string partSpecifier, CancellationToken cancellationToken);
    }
}
