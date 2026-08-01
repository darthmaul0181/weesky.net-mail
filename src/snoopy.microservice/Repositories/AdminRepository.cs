using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class AdminRepository(
    ApplicationDbContext context,
    IWebmailUserStore webmailUsers,
    IMemoryCache cache,
    ILogger<AdminRepository> logger) : IAdminRepository
{
    private const int MinPasswordLength = 8;
    private const int DefaultQuotaMb = 1024;

    /// <summary>
    /// How long the admin flag is reused across requests, kept equal to
    /// <see cref="SessionGuard.CacheWindow"/>: both bound how long an
    /// account state change made outside this process keeps being answered from memory. Changes
    /// made through this repository drop the entry at once.
    /// </summary>
    internal static readonly TimeSpan CacheWindow = SessionGuard.CacheWindow;

    public async Task<bool> IsAdminAsync(string username, string domainName)
    {
        return await cache.GetOrCreateAsync(AdminFlagKey(username, domainName), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheWindow;

            return await (from user in context.Users.AsNoTracking()
                          join domain in context.Domains on user.DomainId equals domain.Id
                          where domain.Name == domainName &&
                                string.Equals(user.Name, username, StringComparison.InvariantCultureIgnoreCase)
                          select user.Admin == ActiveState.Y)
                .FirstOrDefaultAsync();
        });
    }

    public async Task<IEnumerable<AdminUserInfo>> GetAllUsersAsync()
    {
        // Projected, not materialised as entities: the admin list needs eight columns and the
        // password is not one of them, so it never reaches memory or the change tracker.
        var users = await (from user in context.Users.AsNoTracking()
                           join domain in context.Domains on user.DomainId equals domain.Id
                           select new AdminUserRow(user.Id, user.Name, user.DomainId, domain.Name,
                               user.FullName, user.QuotaMb, user.Active, user.Admin))
            .ToListAsync();

        // LastLogins keys on the full email; only fetch rows for users we are returning
        // (skips stale rows of deleted accounts instead of loading the whole table).
        var emails = users.Select(u => u.Email).ToList();
        var loginsByUser = (await context.LastLogins.AsNoTracking()
                .Where(l => emails.Contains(l.UserId))
                .ToListAsync())
            .GroupBy(l => l.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return users
            .Select(u => u.ToInfo(BuildLastLogins(loginsByUser, u.Email)))
            .ToList();
    }

    public async Task<AdminUserInfo?> GetUserByIdAsync(int id)
    {
        var row = await (from user in context.Users
                         join domain in context.Domains on user.DomainId equals domain.Id
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

        var domain = await context.Domains.FirstOrDefaultAsync(d => d.Id == request.DomainId);
        if (domain == null)
            return Result.Failure<AdminUserInfo>($"Domain '{request.DomainId}' not found");

        bool duplicate = await context.Users.AnyAsync(u =>
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
            QuotaMb = request.QuotaMb ?? DefaultQuotaMb,
            Active = State(request.Active ?? true),
            Admin = State(request.Admin ?? false),
            LastUpdate = DateTime.UtcNow
        };

        context.Users.Add(newUser);
        await context.SaveChangesAsync();

        return Result.Success(MapToAdminUserInfo(newUser, domain.Name));
    }

    public async Task<Result<AdminUserInfo>> UpdateUserAsync(int id, AdminUserRequest request)
    {
        if (!string.IsNullOrEmpty(request.Password) && request.Password.Length < MinPasswordLength)
            return Result.Failure<AdminUserInfo>($"Password must contain at least {MinPasswordLength} characters");

        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null)
            return Result.Failure<AdminUserInfo>($"User with id {id} not found");

        var domain = await context.Domains.FirstOrDefaultAsync(d => d.Id == user.DomainId);

        // Absent means "leave it alone", never "set it to the default": a PUT that omits
        // quota or the admin flag must not reset the quota nor revoke the role.
        user.FullName = request.FullName ?? user.FullName;
        if (request.QuotaMb is { } quota) user.QuotaMb = quota;
        if (request.Active is { } active) user.Active = State(active);
        if (request.Admin is { } admin) user.Admin = State(admin);

        if (!string.IsNullOrEmpty(request.Password))
        {
            user.Password = request.Password;
            user.LastUpdate = DateTime.UtcNow;
        }

        await context.SaveChangesAsync();
        ForgetAdminFlag(user.Name, domain?.Name);

        return Result.Success(MapToAdminUserInfo(user, domain?.Name ?? user.DomainId));
    }

    public async Task<Result> DeleteUserAsync(int id)
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null)
            return Result.Failure($"User with id {id} not found");

        var domain = await context.Domains.FirstOrDefaultAsync(d => d.Id == user.DomainId);

        context.Users.Remove(user);
        await context.SaveChangesAsync();
        ForgetAdminFlag(user.Name, domain?.Name);

        if (domain is not null)
        {
            var email = $"{user.Name}@{domain.Name}".Trim().ToLowerInvariant();
            try
            {
                await webmailUsers.DeleteByEmailAsync(email, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Best-effort: the dovecot account is already gone; a webmail-DB failure must not
                // fail the deletion. Orphan preference rows are recoverable; a failed request is not.
                logger.LogWarning(ex, "Webmail user row for {Email} could not be deleted after account removal", email);
            }
        }

        return Result.Success();
    }

    public async Task<IEnumerable<Domain>> GetAllDomainsAsync()
    {
        return await context.Domains
            .Select(d => new Domain { Id = d.Id, Name = d.Name })
            .ToListAsync();
    }

    public async Task<Result<Domain>> CreateDomainAsync(AdminDomainRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Id) || request.Id.Length > 3)
            return Result.Failure<Domain>("Domain id must be 1-3 characters");

        if (string.IsNullOrWhiteSpace(request.Name))
            return Result.Failure<Domain>("Domain name is required");

        if (await context.Domains.AnyAsync(d => d.Id == request.Id))
            return Result.Failure<Domain>($"Domain id '{request.Id}' already exists");

        var domain = new MailDomain { Id = request.Id.ToUpperInvariant(), Name = request.Name };
        context.Domains.Add(domain);
        await context.SaveChangesAsync();

        return Result.Success(new Domain { Id = domain.Id, Name = domain.Name });
    }

    public async Task<Result<Domain>> UpdateDomainAsync(string id, AdminDomainRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result.Failure<Domain>("Domain name is required");

        var domain = await context.Domains.FirstOrDefaultAsync(d => d.Id == id);
        if (domain == null)
            return Result.Failure<Domain>($"Domain '{id}' not found");

        domain.Name = request.Name;
        await context.SaveChangesAsync();

        return Result.Success(new Domain { Id = domain.Id, Name = domain.Name });
    }

    public async Task<Result> DeleteDomainAsync(string id)
    {
        var domain = await context.Domains.FirstOrDefaultAsync(d => d.Id == id);
        if (domain == null)
            return Result.Failure($"Domain '{id}' not found");

        if (await context.Users.AnyAsync(u => u.DomainId == id))
            return Result.Failure($"Cannot delete domain '{id}': it still has associated users");

        context.Domains.Remove(domain);
        await context.SaveChangesAsync();
        return Result.Success();
    }

    public async Task<IEnumerable<VirtualDomainInfo>> GetAllVirtualDomainsAsync()
    {
        // "No mailbox lives here, or someone was given it" as two EXISTS: the equivalent client-side
        // filter read the whole users and ownerships tables to answer it.
        var aliasDomains = await context.Domains
            .Where(d => !context.Users.Any(u => u.DomainId == d.Id) ||
                        context.DomainsOwnerships.Any(o => o.DomainId == d.Id))
            .Select(d => new { d.Id, d.Name })
            .ToListAsync();

        // One query for every owner, grouped in memory: the per-domain lookup this replaces
        // issued a round trip per alias domain.
        var ids = aliasDomains.Select(d => d.Id).ToList();
        var ownersByDomain = (await OwnersQuery(o => ids.Contains(o.DomainId)).ToListAsync())
            .GroupBy(x => x.DomainId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Owner).ToList(), StringComparer.Ordinal);

        return aliasDomains
            .Select(domain => new VirtualDomainInfo
            {
                DomainId = domain.Id,
                DomainName = domain.Name,
                Owners = ownersByDomain.TryGetValue(domain.Id, out var owners) ? owners : []
            })
            .ToList();
    }

    public async Task<Result<VirtualDomainInfo>> AddVirtualDomainOwnerAsync(string domainId, int userId)
    {
        var domain = await context.Domains.FirstOrDefaultAsync(d => d.Id == domainId);
        if (domain == null)
            return Result.Failure<VirtualDomainInfo>($"Domain '{domainId}' not found");

        if (!await context.Users.AnyAsync(u => u.Id == userId))
            return Result.Failure<VirtualDomainInfo>($"User with id {userId} not found");

        if (!await context.DomainsOwnerships.AnyAsync(o => o.DomainId == domainId && o.UserId == userId))
        {
            context.DomainsOwnerships.Add(new MailDomainOwnership { DomainId = domainId, UserId = userId });
            await context.SaveChangesAsync();
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
        var ownership = await context.DomainsOwnerships.FirstOrDefaultAsync(o => o.DomainId == domainId && o.UserId == userId);
        if (ownership == null)
            return Result.Failure($"No ownership found for domain '{domainId}' and user {userId}");

        context.DomainsOwnerships.Remove(ownership);
        await context.SaveChangesAsync();
        return Result.Success();
    }

    // ---------- Helpers ----------

    private static ActiveState State(bool on) => on ? ActiveState.Y : ActiveState.N;

    /// <summary>
    /// Keyed on the whole address, so no account's flag can answer for another: '@' cannot occur
    /// in either half, which makes the pair unambiguous.
    /// </summary>
    private static string AdminFlagKey(string username, string domainName) =>
        $"admin-flag:{username.Trim().ToLowerInvariant()}@{domainName.Trim().ToLowerInvariant()}";

    /// <summary>A role granted or revoked here takes effect on the next request, not in a minute.</summary>
    private void ForgetAdminFlag(string username, string? domainName)
    {
        if (domainName is not null) cache.Remove(AdminFlagKey(username, domainName));
    }

    private static AdminUserInfo MapToAdminUserInfo(MailUser user, string domainName, List<LastLoginEntry>? lastLogins = null) =>
        new AdminUserRow(user.Id, user.Name, user.DomainId, domainName,
            user.FullName, user.QuotaMb, user.Active, user.Admin).ToInfo(lastLogins);

    /// <summary>The columns <see cref="AdminUserInfo"/> is built from, and nothing else.</summary>
    private sealed record AdminUserRow(
        int Id, string Name, string DomainId, string DomainName,
        string? FullName, int QuotaMb, ActiveState Active, ActiveState Admin)
    {
        public string Email => $"{Name}@{DomainName}";

        public AdminUserInfo ToInfo(List<LastLoginEntry>? lastLogins = null) => new()
        {
            Id = Id,
            UserName = Name,
            DomainId = DomainId,
            DomainName = DomainName,
            FullName = FullName,
            QuotaMb = QuotaMb,
            Active = Active == ActiveState.Y,
            Admin = Admin == ActiveState.Y,
            LastLogins = lastLogins ?? []
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

    private Task<List<OwnerInfo>> GetDomainOwnersAsync(string domainId) =>
        OwnersQuery(o => o.DomainId == domainId).Select(x => x.Owner).ToListAsync();

    /// <summary>
    /// Owners of the ownerships matching <paramref name="scope"/>, each carrying the domain it
    /// belongs to. One shape for the single-domain read and the grouped listing, so the two
    /// cannot report a different owner set for the same rows.
    /// </summary>
    private IQueryable<DomainOwner> OwnersQuery(
        System.Linq.Expressions.Expression<Func<MailDomainOwnership, bool>> scope) =>
        from ownership in context.DomainsOwnerships.Where(scope)
        join user in context.Users on ownership.UserId equals user.Id
        join domain in context.Domains on user.DomainId equals domain.Id
        select new DomainOwner(
            ownership.DomainId,
            new OwnerInfo { OwnerId = ownership.UserId, OwnerEmail = user.Name + "@" + domain.Name });

    private sealed record DomainOwner(string DomainId, OwnerInfo Owner);
}
