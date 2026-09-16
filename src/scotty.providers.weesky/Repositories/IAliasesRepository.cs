using CSharpFunctionalExtensions;
using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Providers.Weesky.Repositories;

public interface IAliasesRepository
{
    Task<IEnumerable<Alias>> GetAliasesAsync(User user, CancellationToken cancellationToken);
    Task<Result> AddAliasAsync(User user, Alias alias, CancellationToken cancellationToken);
    Task<Result> DeleteAliasAsync(User user, Alias alias, CancellationToken cancellationToken);
}
