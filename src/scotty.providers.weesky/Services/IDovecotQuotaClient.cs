using CSharpFunctionalExtensions;
using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Providers.Weesky.Services;

public interface IDovecotQuotaClient
{
    Task<Result<Quota>> GetQuotaAsync(User user, CancellationToken cancellationToken = default);
}
