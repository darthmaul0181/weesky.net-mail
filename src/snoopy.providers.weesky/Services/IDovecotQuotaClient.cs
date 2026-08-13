using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Providers.Weesky.Services;

public interface IDovecotQuotaClient
{
    Task<Result<Quota>> GetQuotaAsync(User user, CancellationToken cancellationToken = default);
}
