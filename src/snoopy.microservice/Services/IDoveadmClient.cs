using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Services
{
    public interface IDoveadmClient
    {
        Task<Result<Quota>> GetQuotaAsync(User user, CancellationToken cancellationToken = default);
        Task<Result<IReadOnlyList<string>>> GetMailboxesAsync(User user, CancellationToken cancellationToken = default);

        /// <summary>Flushes the Dovecot auth cache entry for a single user.</summary>
        Task<Result> FlushAuthCacheAsync(string email, CancellationToken cancellationToken = default);

        /// <summary>Flushes the entire Dovecot auth cache (all users).</summary>
        Task<Result> FlushAllAuthCacheAsync(CancellationToken cancellationToken = default);
    }
}
