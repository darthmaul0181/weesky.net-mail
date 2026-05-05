using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Authentication.Models;

namespace weesky.Snoopy.Microservice.Authentication.Services
{
	public interface IUserAuthenticator
	{
		Result<AuthToken> Authenticate(string email, string password);
	}
}