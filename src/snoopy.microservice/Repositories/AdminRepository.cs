using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Repositories
{
    public class AdminRepository : IAdminRepository
    {
        private const int MinPasswordLength = 8;

        private readonly ApplicationDbContext _context;

        public AdminRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<bool> IsAdminAsync(string username, string domainName)
        {
            var domain = await _context.Domains.FirstOrDefaultAsync(d => d.Name == domainName);
            if (domain == null) return false;

            var user = await _context.Users.FirstOrDefaultAsync(u =>
                string.Equals(u.Name, username, StringComparison.InvariantCultureIgnoreCase) &&
                u.DomainId == domain.Id);

            return user?.Admin == ActiveState.Y;
        }

        public async Task<IEnumerable<AdminUserInfo>> GetAllUsersAsync()
        {
            var users = await (from user in _context.Users
                               join domain in _context.Domains on user.DomainId equals domain.Id
                               select new { user, domainName = domain.Name })
                .ToListAsync();

            // LastLogins keys on the full email; only fetch rows for users we are returning
            // (skips stale rows of deleted accounts instead of loading the whole table).
            var emails = users.Select(x => x.user.Name + "@" + x.domainName).ToList();
            var loginsByUser = (await _context.LastLogins
                    .Where(l => emails.Contains(l.UserId))
                    .ToListAsync())
                .GroupBy(l => l.UserId)
                .ToDictionary(g => g.Key, g => g.ToList());

            return users
                .Select(x => MapToAdminUserInfo(x.user, x.domainName,
                    BuildLastLogins(loginsByUser, $"{x.user.Name}@{x.domainName}")))
                .ToList();
        }

        public async Task<AdminUserInfo?> GetUserByIdAsync(int id)
        {
            var row = await (from user in _context.Users
                             join domain in _context.Domains on user.DomainId equals domain.Id
                             where user.Id == id
                             select new { user, domain })
                .FirstOrDefaultAsync();

            return row == null ? null : MapToAdminUserInfo(row.user, row.domain.Name);
        }

        public async Task<Result<AdminUserInfo>> CreateUserAsync(AdminUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.UserName))
                return Result.Failure<AdminUserInfo>("Username is required");

            if (string.IsNullOrEmpty(request.Password))
                return Result.Failure<AdminUserInfo>("Password is required");

            if (request.Password.Length < MinPasswordLength)
                return Result.Failure<AdminUserInfo>($"Password must contain at least {MinPasswordLength} characters");

            var domain = await _context.Domains.FirstOrDefaultAsync(d => d.Id == request.DomainId);
            if (domain == null)
                return Result.Failure<AdminUserInfo>($"Domain '{request.DomainId}' not found");

            bool duplicate = await _context.Users.AnyAsync(u =>
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
            await _context.SaveChangesAsync();

            return Result.Success(MapToAdminUserInfo(newUser, domain.Name));
        }

        public async Task<Result<AdminUserInfo>> UpdateUserAsync(int id, AdminUserRequest request)
        {
            if (!string.IsNullOrEmpty(request.Password) && request.Password.Length < MinPasswordLength)
                return Result.Failure<AdminUserInfo>($"Password must contain at least {MinPasswordLength} characters");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
                return Result.Failure<AdminUserInfo>($"User with id {id} not found");

            var domain = await _context.Domains.FirstOrDefaultAsync(d => d.Id == user.DomainId);

            user.FullName = request.FullName ?? user.FullName;
            user.QuotaMb = request.QuotaMb;
            user.Active = request.Active ? ActiveState.Y : ActiveState.N;
            user.Admin = request.Admin ? ActiveState.Y : ActiveState.N;

            if (!string.IsNullOrEmpty(request.Password))
            {
                user.Password = request.Password;
                user.LastUpdate = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return Result.Success(MapToAdminUserInfo(user, domain?.Name ?? user.DomainId));
        }

        public async Task<Result> DeleteUserAsync(int id)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
                return Result.Failure($"User with id {id} not found");

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            return Result.Success();
        }

        public async Task<IEnumerable<Domain>> GetAllDomainsAsync()
        {
            return await _context.Domains
                .Select(d => new Domain { Id = d.Id, Name = d.Name })
                .ToListAsync();
        }

        public async Task<Result<Domain>> CreateDomainAsync(AdminDomainRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Id) || request.Id.Length > 3)
                return Result.Failure<Domain>("Domain id must be 1-3 characters");

            if (string.IsNullOrWhiteSpace(request.Name))
                return Result.Failure<Domain>("Domain name is required");

            if (await _context.Domains.AnyAsync(d => d.Id == request.Id))
                return Result.Failure<Domain>($"Domain id '{request.Id}' already exists");

            var domain = new MailDomain { Id = request.Id.ToUpperInvariant(), Name = request.Name };
            _context.Domains.Add(domain);
            await _context.SaveChangesAsync();

            return Result.Success(new Domain { Id = domain.Id, Name = domain.Name });
        }

        public async Task<Result<Domain>> UpdateDomainAsync(string id, AdminDomainRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Result.Failure<Domain>("Domain name is required");

            var domain = await _context.Domains.FirstOrDefaultAsync(d => d.Id == id);
            if (domain == null)
                return Result.Failure<Domain>($"Domain '{id}' not found");

            domain.Name = request.Name;
            await _context.SaveChangesAsync();

            return Result.Success(new Domain { Id = domain.Id, Name = domain.Name });
        }

        public async Task<Result> DeleteDomainAsync(string id)
        {
            var domain = await _context.Domains.FirstOrDefaultAsync(d => d.Id == id);
            if (domain == null)
                return Result.Failure($"Domain '{id}' not found");

            if (await _context.Users.AnyAsync(u => u.DomainId == id))
                return Result.Failure($"Cannot delete domain '{id}': it still has associated users");

            _context.Domains.Remove(domain);
            await _context.SaveChangesAsync();
            return Result.Success();
        }

        public async Task<IEnumerable<VirtualDomainInfo>> GetAllVirtualDomainsAsync()
        {
            var primaryDomainIds = (await _context.Users.Select(u => u.DomainId).Distinct().ToListAsync()).ToHashSet();
            var ownedDomainIds = (await _context.DomainsOwnerships.Select(o => o.DomainId).ToListAsync()).ToHashSet();

            var aliasDomains = (await _context.Domains
                    .Select(d => new { d.Id, d.Name })
                    .ToListAsync())
                .Where(d => !primaryDomainIds.Contains(d.Id) || ownedDomainIds.Contains(d.Id))
                .ToList();

            var result = new List<VirtualDomainInfo>();
            foreach (var domain in aliasDomains)
            {
                result.Add(new VirtualDomainInfo
                {
                    DomainId = domain.Id,
                    DomainName = domain.Name,
                    Owners = await GetDomainOwnersAsync(domain.Id)
                });
            }

            return result;
        }

        public async Task<Result<VirtualDomainInfo>> AddVirtualDomainOwnerAsync(string domainId, int userId)
        {
            var domain = await _context.Domains.FirstOrDefaultAsync(d => d.Id == domainId);
            if (domain == null)
                return Result.Failure<VirtualDomainInfo>($"Domain '{domainId}' not found");

            if (!await _context.Users.AnyAsync(u => u.Id == userId))
                return Result.Failure<VirtualDomainInfo>($"User with id {userId} not found");

            if (!await _context.DomainsOwnerships.AnyAsync(o => o.DomainId == domainId && o.UserId == userId))
            {
                _context.DomainsOwnerships.Add(new MailDomainOwnership { DomainId = domainId, UserId = userId });
                await _context.SaveChangesAsync();
            }

            return Result.Success(new VirtualDomainInfo
            {
                DomainId = domain.Id,
                DomainName = domain.Name,
                Owners = await GetDomainOwnersAsync(domainId)
            });
        }

        public async Task<Result> RemoveVirtualDomainOwnerAsync(string domainId, int userId)
        {
            var ownership = await _context.DomainsOwnerships.FirstOrDefaultAsync(o => o.DomainId == domainId && o.UserId == userId);
            if (ownership == null)
                return Result.Failure($"No ownership found for domain '{domainId}' and user {userId}");

            _context.DomainsOwnerships.Remove(ownership);
            await _context.SaveChangesAsync();
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

        private Task<List<OwnerInfo>> GetDomainOwnersAsync(string domainId)
        {
            return (from ownership in _context.DomainsOwnerships
                    join user in _context.Users on ownership.UserId equals user.Id
                    join domain in _context.Domains on user.DomainId equals domain.Id
                    where ownership.DomainId == domainId
                    select new OwnerInfo { OwnerId = ownership.UserId, OwnerEmail = user.Name + "@" + domain.Name })
                .ToListAsync();
        }
    }
}
