using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Repositories
{
	public interface IAliasesRepository
	{
		IEnumerable<Alias> GetAliases(User user);
		Result AddAlias(User user, Alias alias);
		Result DeleteAlias(User user, Alias alias);
	}
}