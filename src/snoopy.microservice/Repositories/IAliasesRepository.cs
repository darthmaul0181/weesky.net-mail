using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Repositories;

public interface IAliasesRepository
{
    Task<IEnumerable<Alias>> GetAliasesAsync(User user);
    Task<Result> AddAliasAsync(User user, Alias alias);
    Task<Result> DeleteAliasAsync(User user, Alias alias);
}
