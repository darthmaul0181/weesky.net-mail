using CSharpFunctionalExtensions;
using weesky.Scotty.Microservice.Models.Mail;

namespace weesky.Scotty.Microservice.Services;

/// <summary>Opens an authenticated IMAP session for one account, for one request.</summary>
public interface IImapConnectionFactory
{
    Task<Result<IImapSession>> OpenAsync(MailAccountConnection connection, CancellationToken cancellationToken);
}
