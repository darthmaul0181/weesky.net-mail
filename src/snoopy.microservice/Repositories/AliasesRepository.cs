using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Repositories
{
    public class AliasesRepository : IAliasesRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AliasesRepository> _logger;

        public AliasesRepository(ApplicationDbContext dbContext, ILogger<AliasesRepository> logger)
        {
            _context = dbContext;
            _logger = logger;
        }

        public async Task<IEnumerable<Alias>> GetAliasesAsync(User user)
        {
            var aliases = from alias in _context.Aliases
                          from usr in _context.Users
                          from domain in _context.Domains
                          join aliasDomain in _context.Domains on alias.Domain equals aliasDomain.Id
                          where alias.DestinationUserId == usr.Id
                               && usr.DomainId == domain.Id
                               && usr.Name == user.Name
                               && domain.Name == user.Domain
                               && (alias.Domain == usr.DomainId || _context.DomainsOwnerships.Any(own => own.DomainId == alias.Domain && own.UserId == usr.Id))
                          select new Alias
                          {
                              Name = alias.Name,
                              Domain = aliasDomain.Name
                          };

            return await aliases.ToListAsync();
        }

        public async Task<Result> AddAliasAsync(User user, Alias alias)
        {
            if (alias == null)
                throw new ArgumentNullException("alias");

            if (!await UserOwnsDomainAsync(user, alias.Domain))
            {
                _logger.LogInformation("Audit: add_alias user={User} alias={Alias} outcome=failure reason=domain_not_owned", user.Email, $"{alias.Name}@{alias.Domain}");
                return Result.Failure($"User '{user.Name}@{user.Domain} doesn't own domain '{alias.Domain}'");
            }

            MailUser? mailUser = await GetMailUserByAsync(user);
            MailDomain mailDomain = await GetDomainByAsync(alias.Domain);

            if (mailUser == null || mailDomain == null)
            {
                _logger.LogInformation("Audit: add_alias user={User} alias={Alias} outcome=failure reason=user_or_domain_missing", user.Email, $"{alias.Name}@{alias.Domain}");
                return Result.Failure("Account not found");
            }

            if (await _context.Aliases.AnyAsync(a => string.Equals(a.Name, alias.Name, StringComparison.InvariantCultureIgnoreCase) && string.Equals(mailDomain.Id, a.Domain, StringComparison.InvariantCultureIgnoreCase) && a.DestinationUserId == mailUser.Id))
            {
                _logger.LogInformation("Audit: add_alias user={User} alias={Alias} outcome=failure reason=already_exists", user.Email, $"{alias.Name}@{alias.Domain}");
                return Result.Failure($"Alias '{alias.Name}@{alias.Domain}' already exists for this user");
            }

            _context.Aliases.Add(new MailAlias
            {
                Name = alias.Name,
                Domain = mailDomain.Id,
                DestinationUserId = mailUser.Id,
            });

            await _context.SaveChangesAsync();

            _logger.LogInformation("Audit: add_alias user={User} alias={Alias} outcome=success", user.Email, $"{alias.Name}@{alias.Domain}");
            return Result.Success();
        }

        public async Task<Result> DeleteAliasAsync(User user, Alias alias)
        {
            if (alias == null)
                throw new ArgumentNullException(nameof(alias));

            if (!await UserOwnsDomainAsync(user, alias.Domain))
            {
                _logger.LogInformation("Audit: delete_alias user={User} alias={Alias} outcome=failure reason=domain_not_owned", user.Email, $"{alias.Name}@{alias.Domain}");
                return Result.Failure($"User '{user.Name}@{user.Domain} doesn't own domain '{alias.Domain}'");
            }

            MailUser? mailUser = await GetMailUserByAsync(user);
            MailDomain mailDomain = await GetDomainByAsync(alias.Domain);

            if (mailUser == null || mailDomain == null)
            {
                _logger.LogInformation("Audit: delete_alias user={User} alias={Alias} outcome=failure reason=user_or_domain_missing", user.Email, $"{alias.Name}@{alias.Domain}");
                return Result.Failure("Account not found");
            }

            MailAlias mailAlias = await _context.Aliases.FirstOrDefaultAsync(a => string.Equals(a.Name, alias.Name, StringComparison.InvariantCultureIgnoreCase) && string.Equals(mailDomain.Id, a.Domain, StringComparison.InvariantCultureIgnoreCase) && a.DestinationUserId == mailUser.Id);
            if (mailAlias == null)
            {
                _logger.LogInformation("Audit: delete_alias user={User} alias={Alias} outcome=failure reason=not_found", user.Email, $"{alias.Name}@{alias.Domain}");
                return Result.Failure($"Alias '{alias.Name}@{alias.Domain} not found");
            }

            _context.Aliases.Remove(mailAlias);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Audit: delete_alias user={User} alias={Alias} outcome=success", user.Email, $"{alias.Name}@{alias.Domain}");
            return Result.Success();
        }

        public async Task<bool> UserOwnsDomainAsync(User user, string domainName)
        {
            if (string.Equals(user.Domain, domainName))
            {
                return true;
            }

            MailUser? mailUser = await GetMailUserByAsync(user);
            if (mailUser == null)
            {
                return false;
            }

            MailDomain? requestedDomain = await GetDomainByAsync(domainName);
            if (requestedDomain == null)
            {
                return false;
            }

            return await _context.DomainsOwnerships.AnyAsync(ownership =>
                ownership.UserId == mailUser.Id &&
                string.Equals(ownership.DomainId, requestedDomain.Id, StringComparison.InvariantCultureIgnoreCase));
        }

        private async Task<MailUser?> GetMailUserByAsync(User user)
        {
            var query = from usr in _context.Users
                        from domain in _context.Domains
                        where usr.DomainId == domain.Id
                            && string.Equals(usr.Name, user.Name, StringComparison.InvariantCultureIgnoreCase)
                            && string.Equals(domain.Name, user.Domain, StringComparison.InvariantCultureIgnoreCase)
                        select usr;

            return await query.FirstOrDefaultAsync();
        }

        private async Task<MailDomain> GetDomainByAsync(string name)
        {
            return await _context.Domains.FirstOrDefaultAsync(domain => string.Equals(domain.Name, name, StringComparison.InvariantCultureIgnoreCase));
        }
    }
}
