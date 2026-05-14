using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Repositories
{
    public interface IAdminRepository
    {
        bool IsAdmin(string username, string domainName);
        IEnumerable<AdminUserInfo> GetAllUsers();
        Result<AdminUserInfo> CreateUser(AdminUserRequest request);
        Result<AdminUserInfo> UpdateUser(int id, AdminUserRequest request);
        Result DeleteUser(int id);
        IEnumerable<Domain> GetAllDomains();
        Result<Domain> CreateDomain(AdminDomainRequest request);
        Result<Domain> UpdateDomain(string id, AdminDomainRequest request);
        Result DeleteDomain(string id);
        IEnumerable<VirtualDomainInfo> GetAllVirtualDomains();
        Result<VirtualDomainInfo> AddVirtualDomainOwner(string domainId, int userId);
        Result RemoveVirtualDomainOwner(string domainId, int userId);
    }
}
