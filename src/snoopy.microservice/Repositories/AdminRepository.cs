using System.Diagnostics;
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
    private const int MinPasswordLength = PasswordPolicy.MinimumLength;
    private const int DefaultQuotaMb = 1024;

    /// <summary>
    /// How long the admin flag is reused across requests, kept equal to
    /// <see cref="SessionGuard.CacheWindow"/>: both bound how long an
    /// account state change made outside this process keeps being answered from memory. Changes
    /// made through this repository take effect on the next request.
    /// </summary>
    internal static readonly TimeSpan CacheWindow = SessionGuard.CacheWindow;

    private const string EpochKey = "admin-flag:epoch";

    public async Task<bool> IsAdminAsync(string username, string domainName, CancellationToken cancellationToken)
    {
        // Read before the query, stored with its answer: a write that commits while the query is
        // in flight has no entry to remove yet, so the epoch is what makes that answer unusable.
        var epoch = CurrentEpoch();
        var key = AdminFlagKey(username, domainName);

        if (cache.TryGetValue(key, out CachedAdminFlag cached) && cached.Epoch == epoch)
            return cached.IsAdmin;

        var isAdmin = await ReadAdminFlagAsync(username, domainName, cancellationToken);

        // Publishing after the await, never through a cache factory: an abandoned read throws here
        // and leaves no entry behind, where a factory would store the answer — or the faulted task.
        cache.Set(key, new CachedAdminFlag(isAdmin, epoch), CacheWindow);
        return isAdmin;
    }

    /// <summary>
    /// The domain resolves to a single row, as the two-step lookup this replaces did: joining every
    /// row bearing the name would let an admin namesake in a second one answer for this account.
    /// </summary>
    internal static IQueryable<bool> AdminFlagQuery(ApplicationDbContext context, string username, string domainName) =>
        context.Users.AsNoTracking()
            .Where(u => u.DomainId == context.Domains.Where(d => d.Name == domainName).Select(d => d.Id).FirstOrDefault() &&
                        string.Equals(u.Name, username, StringComparison.InvariantCultureIgnoreCase))
            .Select(u => u.Admin == ActiveState.Y);

    private Task<bool> ReadAdminFlagAsync(string username, string domainName, CancellationToken cancellationToken) =>
        AdminFlagQuery(context, username, domainName).FirstOrDefaultAsync(cancellationToken);

    public async Task<IEnumerable<AdminUserInfo>> GetAllUsersAsync(CancellationToken cancellationToken)
    {
        // Projected, not materialised as entities: the admin list needs eight columns and the
        // password is not one of them, so it never reaches memory or the change tracker.
        var users = await (from user in context.Users.AsNoTracking()
                           join domain in context.Domains on user.DomainId equals domain.Id
                           select new AdminUserRow(user.Id, user.Name, user.DomainId, domain.Name,
                               user.FullName, user.QuotaMb, user.Active, user.Admin))
            .ToListAsync(cancellationToken);

        // LastLogins keys on the full email; only fetch rows for users we are returning
        // (skips stale rows of deleted accounts instead of loading the whole table).
        var emails = users.Select(u => u.Email).ToList();
        var loginsByUser = (await context.LastLogins.AsNoTracking()
                .Where(l => emails.Contains(l.UserId))
                .ToListAsync(cancellationToken))
            .GroupBy(l => l.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return users
            .Select(u => u.ToInfo(BuildLastLogins(loginsByUser, u.Email)))
            .ToList();
    }

    public async Task<AdminUserInfo?> GetUserByIdAsync(int id, CancellationToken cancellationToken)
    {
        var row = await (from user in context.Users
                         join domain in context.Domains on user.DomainId equals domain.Id
                         where user.Id == id
                         select new { user, domain })
            .FirstOrDefaultAsync(cancellationToken);

        return row == null ? null : MapToAdminUserInfo(row.user, row.domain.Name);
    }

    public async Task<Result<AdminUserInfo>> CreateUserAsync(AdminUserRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
            return Result.Failure<AdminUserInfo>("Username is required");

        if (string.IsNullOrEmpty(request.Password))
            return Result.Failure<AdminUserInfo>("Password is required");

        if (request.Password.Length < MinPasswordLength)
            return Result.Failure<AdminUserInfo>($"Password must contain at least {MinPasswordLength} characters");

        var domain = await context.Domains.FirstOrDefaultAsync(d => d.Id == request.DomainId, cancellationToken);
        if (domain == null)
            return Result.Failure<AdminUserInfo>($"Domain '{request.DomainId}' not found");

        bool duplicate = await context.Users.AnyAsync(u =>
            string.Equals(u.Name, request.UserName, StringComparison.InvariantCultureIgnoreCase) &&
            u.DomainId == request.DomainId, cancellationToken);
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
        await context.SaveChangesAsync(cancellationToken);
        ForgetAdminFlags();

        return Result.Success(MapToAdminUserInfo(newUser, domain.Name));
    }

    public async Task<Result<AdminUserInfo>> UpdateUserAsync(int id, AdminUserRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.Password) && request.Password.Length < MinPasswordLength)
            return Result.Failure<AdminUserInfo>($"Password must contain at least {MinPasswordLength} characters");

        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user == null)
            return Result.Failure<AdminUserInfo>($"User with id {id} not found");

        var domain = await context.Domains.FirstOrDefaultAsync(d => d.Id == user.DomainId, cancellationToken);

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

        await context.SaveChangesAsync(cancellationToken);
        ForgetAdminFlags();

        return Result.Success(MapToAdminUserInfo(user, domain?.Name ?? user.DomainId));
    }

    public async Task<Result> DeleteUserAsync(int id, CancellationToken cancellationToken)
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user == null)
            return Result.Failure($"User with id {id} not found");

        var domain = await context.Domains.FirstOrDefaultAsync(d => d.Id == user.DomainId, cancellationToken);

        context.Users.Remove(user);
        await context.SaveChangesAsync(cancellationToken);
        ForgetAdminFlags();

        if (domain is not null)
        {
            var email = $"{user.Name}@{domain.Name}".Trim().ToLowerInvariant();
            try
            {
                // Not the caller's token: the dovecot rows are already committed, so a client
                // disconnect here would orphan the webmail row with nothing left to retry it.
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

    public async Task<IEnumerable<Domain>> GetAllDomainsAsync(CancellationToken cancellationToken)
    {
        // The alias count rides on the listing rather than on a lookup of its own: the delete
        // confirmation is the only consumer, and a second round trip per domain to answer a
        // question the list is already fetching would be the N+1 this repository just lost.
        return await context.Domains
            .Select(d => new Domain
            {
                Id = d.Id,
                Name = d.Name,
                AliasCount = context.Aliases.Count(a => a.Domain == d.Id)
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<Domain>> CreateDomainAsync(AdminDomainRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Id) || request.Id.Length > 3)
            return Result.Failure<Domain>("Domain id must be 1-3 characters");

        if (string.IsNullOrWhiteSpace(request.Name))
            return Result.Failure<Domain>("Domain name is required");

        if (await context.Domains.AnyAsync(d => d.Id == request.Id, cancellationToken))
            return Result.Failure<Domain>($"Domain id '{request.Id}' already exists");

        var domain = new MailDomain { Id = request.Id.ToUpperInvariant(), Name = request.Name };
        context.Domains.Add(domain);
        await context.SaveChangesAsync(cancellationToken);
        ForgetAdminFlags();

        return Result.Success(new Domain { Id = domain.Id, Name = domain.Name });
    }

    public async Task<Result<Domain>> UpdateDomainAsync(string id, AdminDomainRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result.Failure<Domain>("Domain name is required");

        var domain = await context.Domains.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (domain == null)
            return Result.Failure<Domain>($"Domain '{id}' not found");

        // The domain name is half the cache key: a rename moves every flag under it to a key
        // nothing has cached yet, and leaves the old key answering for a name that no longer maps.
        domain.Name = request.Name;
        await context.SaveChangesAsync(cancellationToken);
        ForgetAdminFlags();

        return Result.Success(new Domain { Id = domain.Id, Name = domain.Name });
    }

    public async Task<Result> DeleteDomainAsync(string id, bool deleteAliases, CancellationToken cancellationToken)
    {
        var domain = await context.Domains.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (domain == null)
            return Result.Failure($"Domain '{id}' not found");

        if (await context.Users.AnyAsync(u => u.DomainId == id, cancellationToken))
            return Result.Failure($"Cannot delete domain '{id}': it still has associated users");

        // aliases.source_domain is ON DELETE CASCADE where the users FK is not: the database
        // refuses the case above on its own, and would take these rows with it without a word.
        // Taking them is what the cascade is deliberately for — an alias domain holds aliases by
        // definition, and refusing outright would leave one undeletable, since no screen in this
        // application lists another user's aliases. So this asks, and says how many.
        var aliasCount = await context.Aliases.CountAsync(a => a.Domain == id, cancellationToken);
        if (aliasCount > 0 && !deleteAliases)
            return Result.Failure(
                $"Deleting domain '{id}' would also delete {aliasCount} alias{(aliasCount == 1 ? string.Empty : "es")}");

        context.Domains.Remove(domain);
        await context.SaveChangesAsync(cancellationToken);
        ForgetAdminFlags();

        // The rows are gone and nothing else records them: the count is the only trace left.
        if (aliasCount > 0)
            logger.LogInformation(
                "Audit: delete_domain domain={Domain} aliases_deleted={Count} outcome=success", id, aliasCount);

        return Result.Success();
    }

    public async Task<IEnumerable<VirtualDomainInfo>> GetAllVirtualDomainsAsync(CancellationToken cancellationToken)
    {
        // "No mailbox lives here, or someone was given it" as two EXISTS: the equivalent client-side
        // filter read the whole users and ownerships tables to answer it.
        var aliasDomains = await context.Domains
            .Where(d => !context.Users.Any(u => u.DomainId == d.Id) ||
                        context.DomainsOwnerships.Any(o => o.DomainId == d.Id))
            .Select(d => new { d.Id, d.Name })
            .ToListAsync(cancellationToken);

        // One query for every owner, grouped in memory: the per-domain lookup this replaces
        // issued a round trip per alias domain.
        var ids = aliasDomains.Select(d => d.Id).ToList();
        var ownersByDomain = (await OwnersQuery(o => ids.Contains(o.DomainId)).ToListAsync(cancellationToken))
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

    public async Task<Result<VirtualDomainInfo>> AddVirtualDomainOwnerAsync(string domainId, int userId, CancellationToken cancellationToken)
    {
        var domain = await context.Domains.FirstOrDefaultAsync(d => d.Id == domainId, cancellationToken);
        if (domain == null)
            return Result.Failure<VirtualDomainInfo>($"Domain '{domainId}' not found");

        if (!await context.Users.AnyAsync(u => u.Id == userId, cancellationToken))
            return Result.Failure<VirtualDomainInfo>($"User with id {userId} not found");

        if (!await context.DomainsOwnerships.AnyAsync(o => o.DomainId == domainId && o.UserId == userId, cancellationToken))
        {
            context.DomainsOwnerships.Add(new MailDomainOwnership { DomainId = domainId, UserId = userId });
            await context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new VirtualDomainInfo
        {
            DomainId = domain.Id,
            DomainName = domain.Name,
            Owners = await GetDomainOwnersAsync(domainId, cancellationToken)
        });
    }

    public async Task<Result> RemoveVirtualDomainOwnerAsync(string domainId, int userId, CancellationToken cancellationToken)
    {
        var ownership = await context.DomainsOwnerships.FirstOrDefaultAsync(o => o.DomainId == domainId && o.UserId == userId, cancellationToken);
        if (ownership == null)
            return Result.Failure($"No ownership found for domain '{domainId}' and user {userId}");

        context.DomainsOwnerships.Remove(ownership);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ---------- Helpers ----------

    private static ActiveState State(bool on) => on ? ActiveState.Y : ActiveState.N;

    /// <summary>
    /// One key per account, folding exactly what the query folds and nothing more: the username
    /// because it is compared lower-cased on both sides, the domain not at all since it is compared
    /// under the database collation. A name differing by a space is another row, so another key.
    /// The halves cannot run together because <see cref="Models.User"/> refuses an address that is
    /// not exactly two '@'-separated parts, and every claim is built from one.
    /// </summary>
    private static string AdminFlagKey(string username, string domainName) =>
        $"admin-flag:{username.ToLowerInvariant()}@{domainName}";

    /// <summary>
    /// Invalidates every cached flag, including answers still being computed. One epoch for all of
    /// them: a write here is rare, and a per-key removal cannot reach an entry that does not exist
    /// yet, which is exactly the entry a revocation must not leave behind.
    /// </summary>
    private void ForgetAdminFlags() =>
        cache.Set(EpochKey, Stopwatch.GetTimestamp(), new MemoryCacheEntryOptions { Priority = CacheItemPriority.NeverRemove });

    /// <summary>
    /// A timestamp rather than a counter, so nothing has to be read before it is written: two
    /// writers cannot lose each other's bump, and an epoch that went missing comes back larger than
    /// any a live entry carries, which costs a re-read instead of serving a revoked flag.
    /// </summary>
    private long CurrentEpoch() => cache.GetOrCreate(EpochKey, entry =>
    {
        entry.Priority = CacheItemPriority.NeverRemove;
        return Stopwatch.GetTimestamp();
    });

    private readonly record struct CachedAdminFlag(bool IsAdmin, long Epoch);

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

    private Task<List<OwnerInfo>> GetDomainOwnersAsync(string domainId, CancellationToken cancellationToken) =>
        OwnersQuery(o => o.DomainId == domainId).Select(x => x.Owner).ToListAsync(cancellationToken);

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
