using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class AliasesRepository(ApplicationDbContext context, ILogger<AliasesRepository> logger) : IAliasesRepository
{
    public async Task<IEnumerable<Alias>> GetAliasesAsync(User user, CancellationToken cancellationToken)
    {
        var aliases = from alias in context.Aliases
                      from usr in context.Users
                      from domain in context.Domains
                      join aliasDomain in context.Domains on alias.Domain equals aliasDomain.Id
                      where alias.DestinationUserId == usr.Id
                           && usr.DomainId == domain.Id
                           && usr.Name == user.Name
                           && domain.Name == user.Domain
                           && (alias.Domain == usr.DomainId || context.DomainsOwnerships.Any(own => own.DomainId == alias.Domain && own.UserId == usr.Id))
                      select new Alias
                      {
                          Name = alias.Name,
                          Domain = aliasDomain.Name
                      };

        return await aliases.ToListAsync(cancellationToken);
    }

    public async Task<Result> AddAliasAsync(User user, Alias alias, CancellationToken cancellationToken)
    {
        if (alias == null)
            throw new ArgumentNullException("alias");

        if (!await UserOwnsDomainAsync(user, alias.Domain, cancellationToken))
        {
            logger.LogInformation("Audit: add_alias user={User} alias={Alias} outcome=failure reason=domain_not_owned", user.Email, $"{alias.Name}@{alias.Domain}");
            return Result.Failure($"User '{user.Name}@{user.Domain} doesn't own domain '{alias.Domain}'");
        }

        MailUser? mailUser = await GetMailUserByAsync(user, cancellationToken);
        MailDomain? mailDomain = await GetDomainByAsync(alias.Domain, cancellationToken);

        if (mailUser == null || mailDomain == null)
        {
            logger.LogInformation("Audit: add_alias user={User} alias={Alias} outcome=failure reason=user_or_domain_missing", user.Email, $"{alias.Name}@{alias.Domain}");
            return Result.Failure("Account not found");
        }

        if (await context.Aliases.AnyAsync(a => string.Equals(a.Name, alias.Name, StringComparison.InvariantCultureIgnoreCase) && string.Equals(mailDomain.Id, a.Domain, StringComparison.InvariantCultureIgnoreCase) && a.DestinationUserId == mailUser.Id, cancellationToken))
        {
            logger.LogInformation("Audit: add_alias user={User} alias={Alias} outcome=failure reason=already_exists", user.Email, $"{alias.Name}@{alias.Domain}");
            return Result.Failure($"Alias '{alias.Name}@{alias.Domain}' already exists for this user");
        }

        context.Aliases.Add(new MailAlias
        {
            Name = alias.Name,
            Domain = mailDomain.Id,
            DestinationUserId = mailUser.Id,
        });

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Audit: add_alias user={User} alias={Alias} outcome=success", user.Email, $"{alias.Name}@{alias.Domain}");
        return Result.Success();
    }

    public async Task<Result> DeleteAliasAsync(User user, Alias alias, CancellationToken cancellationToken)
    {
        if (alias == null)
            throw new ArgumentNullException(nameof(alias));

        if (!await UserOwnsDomainAsync(user, alias.Domain, cancellationToken))
        {
            logger.LogInformation("Audit: delete_alias user={User} alias={Alias} outcome=failure reason=domain_not_owned", user.Email, $"{alias.Name}@{alias.Domain}");
            return Result.Failure($"User '{user.Name}@{user.Domain} doesn't own domain '{alias.Domain}'");
        }

        MailUser? mailUser = await GetMailUserByAsync(user, cancellationToken);
        MailDomain? mailDomain = await GetDomainByAsync(alias.Domain, cancellationToken);

        if (mailUser == null || mailDomain == null)
        {
            logger.LogInformation("Audit: delete_alias user={User} alias={Alias} outcome=failure reason=user_or_domain_missing", user.Email, $"{alias.Name}@{alias.Domain}");
            return Result.Failure("Account not found");
        }

        MailAlias? mailAlias = await context.Aliases.FirstOrDefaultAsync(a => string.Equals(a.Name, alias.Name, StringComparison.InvariantCultureIgnoreCase) && string.Equals(mailDomain.Id, a.Domain, StringComparison.InvariantCultureIgnoreCase) && a.DestinationUserId == mailUser.Id, cancellationToken);
        if (mailAlias == null)
        {
            logger.LogInformation("Audit: delete_alias user={User} alias={Alias} outcome=failure reason=not_found", user.Email, $"{alias.Name}@{alias.Domain}");
            return Result.Failure($"Alias '{alias.Name}@{alias.Domain} not found");
        }

        context.Aliases.Remove(mailAlias);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Audit: delete_alias user={User} alias={Alias} outcome=success", user.Email, $"{alias.Name}@{alias.Domain}");
        return Result.Success();
    }

    public async Task<bool> UserOwnsDomainAsync(User user, string domainName, CancellationToken cancellationToken)
    {
        if (string.Equals(user.Domain, domainName))
        {
            return true;
        }

        MailUser? mailUser = await GetMailUserByAsync(user, cancellationToken);
        if (mailUser == null)
        {
            return false;
        }

        MailDomain? requestedDomain = await GetDomainByAsync(domainName, cancellationToken);
        if (requestedDomain == null)
        {
            return false;
        }

        return await context.DomainsOwnerships.AnyAsync(ownership =>
            ownership.UserId == mailUser.Id &&
            string.Equals(ownership.DomainId, requestedDomain.Id, StringComparison.InvariantCultureIgnoreCase), cancellationToken);
    }

    private async Task<MailUser?> GetMailUserByAsync(User user, CancellationToken cancellationToken)
    {
        var query = from usr in context.Users
                    from domain in context.Domains
                    where usr.DomainId == domain.Id
                        && string.Equals(usr.Name, user.Name, StringComparison.InvariantCultureIgnoreCase)
                        && string.Equals(domain.Name, user.Domain, StringComparison.InvariantCultureIgnoreCase)
                    select usr;

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<MailDomain?> GetDomainByAsync(string name, CancellationToken cancellationToken)
    {
        return await context.Domains.FirstOrDefaultAsync(domain => string.Equals(domain.Name, name, StringComparison.InvariantCultureIgnoreCase), cancellationToken);
    }
}
