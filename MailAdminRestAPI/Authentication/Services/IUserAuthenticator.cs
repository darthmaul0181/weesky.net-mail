using weesky.MailAdminRestAPI.Authentication.Models;
using weesky.MailAdminRestAPI.Data;

namespace weesky.MailAdminRestAPI.Authentication.Services
{
	public interface IUserAuthenticator
	{
		AuthResult Authenticate(string email, string password);
	}
}