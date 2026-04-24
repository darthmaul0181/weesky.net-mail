using CSharpFunctionalExtensions;
using weesky.MailAdminRestAPI.Models;

namespace weesky.MailAdminRestAPI.Services
{
    public interface IDovecotQuotaClient
    {
        Task<Result<Quota>> GetQuotaAsync(User user, CancellationToken cancellationToken = default);
    }
}
