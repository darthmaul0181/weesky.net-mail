using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Repositories;

public interface IAdminRepository
{
    Task<bool> IsAdminAsync(string username, string domainName, CancellationToken cancellationToken);
    Task<IEnumerable<AdminUserInfo>> GetAllUsersAsync(CancellationToken cancellationToken);
    Task<AdminUserInfo?> GetUserByIdAsync(int id, CancellationToken cancellationToken);
    Task<Result<AdminUserInfo>> CreateUserAsync(AdminUserRequest request, CancellationToken cancellationToken);
    Task<Result<AdminUserInfo>> UpdateUserAsync(int id, AdminUserRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteUserAsync(int id, CancellationToken cancellationToken);
    Task<IEnumerable<Domain>> GetAllDomainsAsync(CancellationToken cancellationToken);
    Task<Result<Domain>> CreateDomainAsync(AdminDomainRequest request, CancellationToken cancellationToken);
    Task<Result<Domain>> UpdateDomainAsync(string id, AdminDomainRequest request, CancellationToken cancellationToken);
    /// <remarks>
    /// <c>deleteAliases</c> acknowledges that the aliases anchored on the domain go with it —
    /// their foreign key cascades. False refuses rather than destroying them silently, and the
    /// refusal names how many there are, since that count is all a confirmation has to go on.
    /// </remarks>
    Task<Result> DeleteDomainAsync(string id, bool deleteAliases, CancellationToken cancellationToken);
    Task<IEnumerable<VirtualDomainInfo>> GetAllVirtualDomainsAsync(CancellationToken cancellationToken);
    Task<Result<VirtualDomainInfo>> AddVirtualDomainOwnerAsync(string domainId, int userId, CancellationToken cancellationToken);
    Task<Result> RemoveVirtualDomainOwnerAsync(string domainId, int userId, CancellationToken cancellationToken);
}
