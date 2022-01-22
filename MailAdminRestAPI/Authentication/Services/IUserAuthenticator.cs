using CSharpFunctionalExtensions;
using weesky.MailAdminRestAPI.Authentication.Models;

namespace weesky.MailAdminRestAPI.Authentication.Services
{
	public interface IUserAuthenticator
	{
		Result<AuthToken> Authenticate(string email, string password);
	}
}