using weesky.MailAdminRestAPI.Models;

namespace weesky.MailAdminRestAPI.Repositories
{
	public interface IUsersRepository
	{
		User FindByEmail(string email);
		bool IsValidPassword(User user, string password);
	}
}