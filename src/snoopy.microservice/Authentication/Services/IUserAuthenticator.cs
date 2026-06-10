using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Authentication.Models;

namespace weesky.Snoopy.Microservice.Authentication.Services
{
    public interface IUserAuthenticator
    {
        Task<Result<AuthToken>> AuthenticateAsync(string email, string password);
    }
}
