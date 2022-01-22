using CSharpFunctionalExtensions;
using weesky.MailAdminRestAPI.Models;

namespace weesky.MailAdminRestAPI.Repositories
{
	public interface IAliasesRepository
	{
		IEnumerable<Alias> GetAliases(User user);
		Result AddAlias(User user, Alias alias);
		Result DeleteAlias(User user, Alias alias);
	}
}