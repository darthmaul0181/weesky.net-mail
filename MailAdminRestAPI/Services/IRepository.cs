using weesky.MailAdminRestAPI.Models;

namespace weesky.MailAdminRestAPI.Services
{
	public interface IRepository
	{
		User FindByEmail(string email);
		bool IsValidPassword(User user, string password);
		IEnumerable<Alias> GetAliases(User user);
		RepositoryResult AddAlias(User user, Alias alias);
		RepositoryResult DeleteAlias(User user, Alias alias);
	}
}