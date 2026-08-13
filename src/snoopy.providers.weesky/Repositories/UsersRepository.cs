using System.Security.Cryptography;
using System.Text;
using CSharpFunctionalExtensions;
using CryptSharp.Core;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Providers.Weesky.Data;

namespace weesky.Snoopy.Providers.Weesky.Repositories;

internal sealed class UsersRepository(ApplicationDbContext context, ILogger<UsersRepository> logger) : IUsersRepository
{
    /// <summary>
    /// Resolves a *usable* account: a deactivated one (<c>active = 'N'</c>) reads as absent.
    /// Both authentication paths funnel through here — the login itself and the per-request
    /// existence check in <c>OnTokenValidated</c> — so filtering once closes both. Dovecot
    /// already refuses IMAP for such an account; without this, everything that does not go
    /// through the mail server (aliases, preferences, admin, and Sieve rules, which
    /// authenticate as the master user) kept working for a disabled mailbox.
    /// </summary>
    public async Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        string[] emailParts = email.Split('@');

        if (emailParts.Length != 2)
        {
            return null;
        }

        var match = await FindMailUserAsync(emailParts[0], emailParts[1], cancellationToken);
        if (match == null || match.Value.MailUser.Active != ActiveState.Y)
        {
            return null;
        }

        return new User($"{match.Value.MailUser.Name}@{match.Value.Domain.Name}")
        {
            FullName = match.Value.MailUser.FullName
        };
    }

    public async Task<Result<AccountInfo>> GetAccountInfoAsync(User user, CancellationToken cancellationToken)
    {
        var match = await FindMailUserAsync(user.Name, user.Domain, cancellationToken);
        if (match == null)
            return Result.Failure<AccountInfo>("Account not found");

        var (mailUser, domain) = match.Value;

        var ownedDomains = await context.DomainsOwnerships
            .Where(o => o.UserId == mailUser.Id)
            .Join(context.Domains, o => o.DomainId, d => d.Id, (o, d) => new Domain { Id = d.Id, Name = d.Name })
            .ToListAsync(cancellationToken);

        if (ownedDomains.All(d => d.Id != domain.Id))
            ownedDomains.Add(new Domain { Id = domain.Id, Name = domain.Name });

        return Result.Success(new AccountInfo
        {
            UserId = mailUser.Id,
            UserName = mailUser.Name,
            FullName = mailUser.FullName,
            Mailbox = mailUser.DomainId,
            Domains = ownedDomains,
            IsAdmin = mailUser.Admin == ActiveState.Y
        });
    }

    public async Task<Result> ChangeFullNameAsync(User user, string fullName, CancellationToken cancellationToken)
    {
        var match = await FindMailUserAsync(user.Name, user.Domain, cancellationToken);
        if (match == null)
        {
            logger.LogInformation("Audit: change_fullname user={User} outcome=failure reason=account_not_found", user.Email);
            return Result.Failure($"User {user.Name}@{user.Domain} not found");
        }

        match.Value.MailUser.FullName = fullName;
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Audit: change_fullname user={User} outcome=success", user.Email);
        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(User user, string newPassword, string oldPassword, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < PasswordPolicy.MinimumLength)
        {
            logger.LogInformation("Audit: change_password user={User} outcome=failure reason=weak_password", user.Email);
            return Result.Failure($"Your password should contains {PasswordPolicy.MinimumLength} chars at least");
        }

        var match = await FindMailUserAsync(user.Name, user.Domain, cancellationToken);
        if (match == null)
        {
            logger.LogInformation("Audit: change_password user={User} outcome=failure reason=account_not_found", user.Email);
            return Result.Failure($"User {user.Name}@{user.Domain} not found");
        }

        MailUser mailUser = match.Value.MailUser;

        if (!PasswordMatches(mailUser.Password, oldPassword))
        {
            logger.LogInformation("Audit: change_password user={User} outcome=failure reason=bad_old_password", user.Email);
            return Result.Failure($"Invalid password");
        }

        mailUser.Password = newPassword;
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Audit: change_password user={User} outcome=success", user.Email);
        return Result.Success();
    }

    /// <summary>
    /// The mailbox and its domain in one round trip, the user name matched case-insensitively.
    /// Null when there is no such mailbox.
    /// </summary>
    /// <remarks>
    /// One joined query rather than the two sequential reads this replaced — domain, then user.
    /// Beyond the saved round trip that halves the cost of every login, a single query is what
    /// makes an unknown domain cost the same as an unknown mailbox: with two, the first returning
    /// nothing skipped the second, and the response time said which of the two it was.
    /// </remarks>
    private async Task<(MailUser MailUser, MailDomain Domain)?> FindMailUserAsync(string name, string domainName, CancellationToken cancellationToken)
    {
        var row = await (from mailUser in context.Users
                         join domain in context.Domains on mailUser.DomainId equals domain.Id
                         where domain.Name == domainName
                            && string.Equals(mailUser.Name, name, StringComparison.InvariantCultureIgnoreCase)
                         select new { MailUser = mailUser, Domain = domain })
            .FirstOrDefaultAsync(cancellationToken);

        return row == null ? null : (row.MailUser, row.Domain);
    }

    /// <summary>
    /// Verifies a password against a stored crypt hash, in time that does not depend on how much
    /// of the hash matched. An ordinary string comparison returns at the first differing byte,
    /// which times how far a guess got.
    /// </summary>
    private static bool PasswordMatches(string storedHash, string password)
    {
        var computed = Crypter.Sha512.Crypt(password, storedHash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(storedHash));
    }
}
