using weesky.MailAdminRestAPI.Authentication.Models;
using weesky.MailAdminRestAPI.Models;

namespace weesky.MailAdminRestAPI.Authentication.Services
{
	public interface ITokenManager
	{
		AuthToken Generate(User user);
	}
}
