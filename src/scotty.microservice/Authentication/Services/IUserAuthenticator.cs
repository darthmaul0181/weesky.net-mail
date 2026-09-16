using CSharpFunctionalExtensions;
using weesky.Scotty.Microservice.Authentication.Models;

namespace weesky.Scotty.Microservice.Authentication.Services;

public interface IUserAuthenticator
{
    Task<Result<AuthToken>> AuthenticateAsync(string email, string password, CancellationToken cancellationToken);
}
