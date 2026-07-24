using CSharpFunctionalExtensions;

namespace weesky.Snoopy.Microservice.Services;

public interface ISmtpConnectionFactory
{
    /// <summary>Opens an authenticated SMTP session with the user's own credentials.</summary>
    Task<Result<ISmtpSession>> OpenAsync(string email, string password, CancellationToken cancellationToken);
}
