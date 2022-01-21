using weesky.MailAdminRestAPI.Models;
using weesky.MailAdminRestAPI.Services;

namespace weesky.MailAdminRestAPI.Repositories
{
	public interface IAliasesRepository
	{
		IEnumerable<Alias> GetAliases(User user);
		ResultEnveloppe AddAlias(User user, Alias alias);
		ResultEnveloppe DeleteAlias(User user, Alias alias);
	}
}