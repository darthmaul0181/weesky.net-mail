using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Services;

public interface IDovecotQuotaClient
{
    Task<Result<Quota>> GetQuotaAsync(User user, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<string>>> GetMailboxesAsync(User user, CancellationToken cancellationToken = default);
}
