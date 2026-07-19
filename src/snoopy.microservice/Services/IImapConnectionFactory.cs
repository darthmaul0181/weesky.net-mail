using CSharpFunctionalExtensions;

namespace weesky.Snoopy.Microservice.Services
{
    /// <summary>Opens an authenticated IMAP session for one user, for one request.</summary>
    public interface IImapConnectionFactory
    {
        Task<Result<IImapSession>> OpenAsync(string email, string password, CancellationToken cancellationToken);
    }
}
