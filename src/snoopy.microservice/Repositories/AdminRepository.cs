using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Repositories
{
    public class AdminRepository : IAdminRepository
    {
        private readonly ApplicationDbContext _context;

        public AdminRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public bool IsAdmin(string username, string domainName)
        {
            var domain = _context.Domains.FirstOrDefault(d => d.Name == domainName);
            if (domain == null) return false;

            var user = _context.Users.FirstOrDefault(u =>
                string.Equals(u.Name, username, StringComparison.InvariantCultureIgnoreCase) &&
                u.DomainId == domain.Id);

            return user?.Admin == ActiveState.Y;
        }

        public IEnumerable<AdminUserInfo> GetAllUsers()
        {
            return (from user in _context.Users
                    join domain in _context.Domains on user.DomainId equals domain.Id
                    select new AdminUserInfo
                    {
                        Id = user.Id,
                        UserName = user.Name,
                        DomainId = user.DomainId,
                        DomainName = domain.Name,
                        FullName = user.FullName,
                        QuotaMb = user.QuotaMb,
                        Active = user.Active == ActiveState.Y,
                        Admin = user.Admin == ActiveState.Y
                    }).ToList();
        }

        public Result<AdminUserInfo> CreateUser(AdminUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.UserName))
                return Result.Failure<AdminUserInfo>("Username is required");

            if (string.IsNullOrEmpty(request.Password))
                return Result.Failure<AdminUserInfo>("Password is required");

            var domain = _context.Domains.FirstOrDefault(d => d.Id == request.DomainId);
            if (domain == null)
                return Result.Failure<AdminUserInfo>($"Domain '{request.DomainId}' not found");

            bool duplicate = _context.Users.Any(u =>
                string.Equals(u.Name, request.UserName, StringComparison.InvariantCultureIgnoreCase) &&
                u.DomainId == request.DomainId);
            if (duplicate)
                return Result.Failure<AdminUserInfo>($"User '{request.UserName}@{domain.Name}' already exists");

            var newUser = new MailUser
            {
                Name = request.UserName.ToLowerInvariant(),
                DomainId = request.DomainId,
                Password = request.Password,
                FullName = request.FullName ?? string.Empty,
                QuotaMb = request.QuotaMb,
                Active = request.Active ? ActiveState.Y : ActiveState.N,
                Admin = request.Admin ? ActiveState.Y : ActiveState.N,
                LastUpdate = DateTime.UtcNow
            };

            _context.Users.Add(newUser);
            _context.SaveChanges();

            return Result.Success(new AdminUserInfo
            {
                Id = newUser.Id,
                UserName = newUser.Name,
                DomainId = newUser.DomainId,
                DomainName = domain.Name,
                FullName = newUser.FullName,
                QuotaMb = newUser.QuotaMb,
                Active = newUser.Active == ActiveState.Y,
                Admin = newUser.Admin == ActiveState.Y
            });
        }

        public Result<AdminUserInfo> UpdateUser(int id, AdminUserRequest request)
        {
            var user = _context.Users.FirstOrDefault(u => u.Id == id);
            if (user == null)
                return Result.Failure<AdminUserInfo>($"User with id {id} not found");

            var domain = _context.Domains.FirstOrDefault(d => d.Id == user.DomainId);

            user.FullName = request.FullName ?? user.FullName;
            user.QuotaMb = request.QuotaMb;
            user.Active = request.Active ? ActiveState.Y : ActiveState.N;
            user.Admin = request.Admin ? ActiveState.Y : ActiveState.N;

            if (!string.IsNullOrEmpty(request.Password))
            {
                user.Password = request.Password;
                user.LastUpdate = DateTime.UtcNow;
            }

            _context.SaveChanges();

            return Result.Success(new AdminUserInfo
            {
                Id = user.Id,
                UserName = user.Name,
                DomainId = user.DomainId,
                DomainName = domain?.Name ?? user.DomainId,
                FullName = user.FullName,
                QuotaMb = user.QuotaMb,
                Active = user.Active == ActiveState.Y,
                Admin = user.Admin == ActiveState.Y
            });
        }

        public Result DeleteUser(int id)
        {
            var user = _context.Users.FirstOrDefault(u => u.Id == id);
            if (user == null)
                return Result.Failure($"User with id {id} not found");

            _context.Users.Remove(user);
            _context.SaveChanges();
            return Result.Success();
        }

        public IEnumerable<Domain> GetAllDomains()
        {
            return _context.Domains
                .Select(d => new Domain { Id = d.Id, Name = d.Name })
                .ToList();
        }

        public Result<Domain> CreateDomain(AdminDomainRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Id) || request.Id.Length > 3)
                return Result.Failure<Domain>("Domain id must be 1-3 characters");

            if (string.IsNullOrWhiteSpace(request.Name))
                return Result.Failure<Domain>("Domain name is required");

            if (_context.Domains.Any(d => d.Id == request.Id))
                return Result.Failure<Domain>($"Domain id '{request.Id}' already exists");

            var domain = new MailDomain { Id = request.Id.ToUpperInvariant(), Name = request.Name };
            _context.Domains.Add(domain);
            _context.SaveChanges();

            return Result.Success(new Domain { Id = domain.Id, Name = domain.Name });
        }

        public Result<Domain> UpdateDomain(string id, AdminDomainRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Result.Failure<Domain>("Domain name is required");

            var domain = _context.Domains.FirstOrDefault(d => d.Id == id);
            if (domain == null)
                return Result.Failure<Domain>($"Domain '{id}' not found");

            domain.Name = request.Name;
            _context.SaveChanges();

            return Result.Success(new Domain { Id = domain.Id, Name = domain.Name });
        }

        public Result DeleteDomain(string id)
        {
            var domain = _context.Domains.FirstOrDefault(d => d.Id == id);
            if (domain == null)
                return Result.Failure($"Domain '{id}' not found");

            if (_context.Users.Any(u => u.DomainId == id))
                return Result.Failure($"Cannot delete domain '{id}': it still has associated users");

            _context.Domains.Remove(domain);
            _context.SaveChanges();
            return Result.Success();
        }

        public IEnumerable<DomainOwnershipInfo> GetAllOwnerships()
        {
            var primaryDomainIds = _context.Users.Select(u => u.DomainId).Distinct().ToHashSet();
            var ownedDomainIds = _context.DomainsOwnerships.Select(o => o.DomainId).ToHashSet();

            var extraDomains = _context.Domains
                .Select(d => new { d.Id, d.Name })
                .ToList()
                .Where(d => !primaryDomainIds.Contains(d.Id) || ownedDomainIds.Contains(d.Id))
                .ToList();

            var ownerships = _context.DomainsOwnerships.ToList();

            var userProjections = _context.Users
                .Select(u => new { u.Id, u.Name, u.DomainId })
                .ToList();

            var domainNames = _context.Domains
                .Select(d => new { d.Id, d.Name })
                .ToDictionary(d => d.Id, d => d.Name);

            return extraDomains.Select(domain =>
            {
                var ownership = ownerships.FirstOrDefault(o => o.DomainId == domain.Id);
                string? ownerEmail = null;
                if (ownership != null)
                {
                    var owner = userProjections.FirstOrDefault(u => u.Id == ownership.UserId);
                    if (owner != null && domainNames.TryGetValue(owner.DomainId, out var ownerDomainName))
                        ownerEmail = $"{owner.Name}@{ownerDomainName}";
                }
                return new DomainOwnershipInfo
                {
                    DomainId = domain.Id,
                    DomainName = domain.Name,
                    OwnerId = ownership?.UserId,
                    OwnerEmail = ownerEmail
                };
            }).ToList();
        }

        public Result<DomainOwnershipInfo> SetOwnership(string domainId, int userId)
        {
            var domain = _context.Domains.FirstOrDefault(d => d.Id == domainId);
            if (domain == null)
                return Result.Failure<DomainOwnershipInfo>($"Domain '{domainId}' not found");

            var user = _context.Users
                .Where(u => u.Id == userId)
                .Select(u => new { u.Id, u.Name, u.DomainId })
                .FirstOrDefault();
            if (user == null)
                return Result.Failure<DomainOwnershipInfo>($"User with id {userId} not found");

            var existing = _context.DomainsOwnerships.FirstOrDefault(o => o.DomainId == domainId);
            if (existing != null)
                existing.UserId = userId;
            else
                _context.DomainsOwnerships.Add(new MailDomainOwnership { DomainId = domainId, UserId = userId });

            _context.SaveChanges();

            var userDomainName = _context.Domains
                .Where(d => d.Id == user.DomainId)
                .Select(d => d.Name)
                .FirstOrDefault();
            return Result.Success(new DomainOwnershipInfo
            {
                DomainId = domain.Id,
                DomainName = domain.Name,
                OwnerId = userId,
                OwnerEmail = userDomainName != null ? $"{user.Name}@{userDomainName}" : null
            });
        }

        public Result DeleteOwnership(string domainId)
        {
            var ownership = _context.DomainsOwnerships.FirstOrDefault(o => o.DomainId == domainId);
            if (ownership == null)
                return Result.Failure($"No ownership found for domain '{domainId}'");

            _context.DomainsOwnerships.Remove(ownership);
            _context.SaveChanges();
            return Result.Success();
        }
    }
}
