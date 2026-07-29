using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>Opens an authenticated IMAP session for one account, for one request.</summary>
public interface IImapConnectionFactory
{
    Task<Result<IImapSession>> OpenAsync(MailAccountConnection connection, CancellationToken cancellationToken);
}
