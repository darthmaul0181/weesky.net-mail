using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Repositories;

public interface IAdminRepository
{
    Task<bool> IsAdminAsync(string username, string domainName);
    Task<IEnumerable<AdminUserInfo>> GetAllUsersAsync();
    Task<AdminUserInfo?> GetUserByIdAsync(int id);
    Task<Result<AdminUserInfo>> CreateUserAsync(AdminUserRequest request);
    Task<Result<AdminUserInfo>> UpdateUserAsync(int id, AdminUserRequest request);
    Task<Result> DeleteUserAsync(int id);
    Task<IEnumerable<Domain>> GetAllDomainsAsync();
    Task<Result<Domain>> CreateDomainAsync(AdminDomainRequest request);
    Task<Result<Domain>> UpdateDomainAsync(string id, AdminDomainRequest request);
    Task<Result> DeleteDomainAsync(string id);
    Task<IEnumerable<VirtualDomainInfo>> GetAllVirtualDomainsAsync();
    Task<Result<VirtualDomainInfo>> AddVirtualDomainOwnerAsync(string domainId, int userId);
    Task<Result> RemoveVirtualDomainOwnerAsync(string domainId, int userId);
}
