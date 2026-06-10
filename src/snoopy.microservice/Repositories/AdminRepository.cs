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
            var loginsByUser = _context.LastLogins
                .ToList()
                .GroupBy(l => l.UserId)
                .ToDictionary(g => g.Key, g => g.ToList());

            return (from user in _context.Users
                    join domain in _context.Domains on user.DomainId equals domain.Id
                    select new { user, domain })
                .ToList()
                .Select(x => MapToAdminUserInfo(x.user, x.domain.Name,
                    BuildLastLogins(loginsByUser, $"{x.user.Name}@{x.domain.Name}")))
                .ToList();
        }

        public AdminUserInfo? GetUserById(int id)
        {
            var row = (from user in _context.Users
                       join domain in _context.Domains on user.DomainId equals domain.Id
                       where user.Id == id
                       select new { user, domain })
                .FirstOrDefault();

            return row == null ? null : MapToAdminUserInfo(row.user, row.domain.Name);
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

            return Result.Success(MapToAdminUserInfo(newUser, domain.Name));
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

            return Result.Success(MapToAdminUserInfo(user, domain?.Name ?? user.DomainId));
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

        public IEnumerable<VirtualDomainInfo> GetAllVirtualDomains()
        {
            var primaryDomainIds = _context.Users.Select(u => u.DomainId).Distinct().ToHashSet();
            var ownedDomainIds = _context.DomainsOwnerships.Select(o => o.DomainId).ToHashSet();

            var aliasDomains = _context.Domains
                .Select(d => new { d.Id, d.Name })
                .ToList()
                .Where(d => !primaryDomainIds.Contains(d.Id) || ownedDomainIds.Contains(d.Id))
                .ToList();

            return aliasDomains
                .Select(domain => new VirtualDomainInfo
                {
                    DomainId = domain.Id,
                    DomainName = domain.Name,
                    Owners = GetDomainOwners(domain.Id)
                })
                .ToList();
        }

        public Result<VirtualDomainInfo> AddVirtualDomainOwner(string domainId, int userId)
        {
            var domain = _context.Domains.FirstOrDefault(d => d.Id == domainId);
            if (domain == null)
                return Result.Failure<VirtualDomainInfo>($"Domain '{domainId}' not found");

            if (!_context.Users.Any(u => u.Id == userId))
                return Result.Failure<VirtualDomainInfo>($"User with id {userId} not found");

            if (!_context.DomainsOwnerships.Any(o => o.DomainId == domainId && o.UserId == userId))
            {
                _context.DomainsOwnerships.Add(new MailDomainOwnership { DomainId = domainId, UserId = userId });
                _context.SaveChanges();
            }

            return Result.Success(new VirtualDomainInfo
            {
                DomainId = domain.Id,
                DomainName = domain.Name,
                Owners = GetDomainOwners(domainId)
            });
        }

        public Result RemoveVirtualDomainOwner(string domainId, int userId)
        {
            var ownership = _context.DomainsOwnerships.FirstOrDefault(o => o.DomainId == domainId && o.UserId == userId);
            if (ownership == null)
                return Result.Failure($"No ownership found for domain '{domainId}' and user {userId}");

            _context.DomainsOwnerships.Remove(ownership);
            _context.SaveChanges();
            return Result.Success();
        }

        // ---------- Helpers ----------

        private static AdminUserInfo MapToAdminUserInfo(MailUser user, string domainName, List<LastLoginEntry>? lastLogins = null)
        {
            return new AdminUserInfo
            {
                Id = user.Id,
                UserName = user.Name,
                DomainId = user.DomainId,
                DomainName = domainName,
                FullName = user.FullName,
                QuotaMb = user.QuotaMb,
                Active = user.Active == ActiveState.Y,
                Admin = user.Admin == ActiveState.Y,
                LastLogins = lastLogins ?? new List<LastLoginEntry>()
            };
        }

        private static List<LastLoginEntry> BuildLastLogins(Dictionary<string, List<LastLogin>> loginsByUser, string email)
        {
            if (!loginsByUser.TryGetValue(email, out var entries))
                return new List<LastLoginEntry>();

            return entries
                .Select(e => new LastLoginEntry
                {
                    Service = e.Service,
                    At = DateTimeOffset.FromUnixTimeSeconds(e.LastAccess).UtcDateTime,
                })
                .OrderByDescending(e => e.At)
                .ToList();
        }

        private List<OwnerInfo> GetDomainOwners(string domainId)
        {
            return (from ownership in _context.DomainsOwnerships
                    join user in _context.Users on ownership.UserId equals user.Id
                    join domain in _context.Domains on user.DomainId equals domain.Id
                    where ownership.DomainId == domainId
                    select new OwnerInfo { OwnerId = ownership.UserId, OwnerEmail = user.Name + "@" + domain.Name })
                .ToList();
        }
    }
}
