using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

public interface ISmtpConnectionFactory
{
    /// <summary>Opens an authenticated SMTP session with the account's own credentials.</summary>
    Task<Result<ISmtpSession>> OpenAsync(MailAccountConnection connection, CancellationToken cancellationToken);
}
