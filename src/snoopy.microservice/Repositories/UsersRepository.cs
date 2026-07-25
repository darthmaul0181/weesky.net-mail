using CryptSharp.Core;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class UsersRepository : IUsersRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<UsersRepository> _logger;

    public UsersRepository(ApplicationDbContext dbContext, ILogger<UsersRepository> logger)
    {
        _context = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a *usable* account: a deactivated one (<c>active = 'N'</c>) reads as absent.
    /// Both authentication paths funnel through here — the login itself and the per-request
    /// existence check in <c>OnTokenValidated</c> — so filtering once closes both. Dovecot
    /// already refuses IMAP for such an account; without this, everything that does not go
    /// through the mail server (aliases, preferences, admin, and Sieve rules, which
    /// authenticate as the master user) kept working for a disabled mailbox.
    /// </summary>
    public async Task<User?> FindByEmailAsync(string email)
    {
        string[] emailParts = email.Split('@');

        if (emailParts.Length != 2)
        {
            return null;
        }

        var match = await FindMailUserAsync(emailParts[0], emailParts[1]);
        if (match == null || match.Value.MailUser.Active != ActiveState.Y)
        {
            return null;
        }

        return new User($"{match.Value.MailUser.Name}@{match.Value.Domain.Name}");
    }

    public async Task<bool> IsValidPasswordAsync(User user, string password)
    {
        var match = await FindMailUserAsync(user.Name, user.Domain);
        return match != null && PasswordMatches(match.Value.MailUser, password);
    }

    public async Task<Result<AccountInfo>> GetAccountInfoAsync(User user)
    {
        var match = await FindMailUserAsync(user.Name, user.Domain);
        if (match == null)
            return Result.Failure<AccountInfo>("Account not found");

        var (mailUser, domain) = match.Value;

        var ownedDomains = await _context.DomainsOwnerships
            .Where(o => o.UserId == mailUser.Id)
            .Join(_context.Domains, o => o.DomainId, d => d.Id, (o, d) => new Domain { Id = d.Id, Name = d.Name })
            .ToListAsync();

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

    public async Task<Result> ChangeFullNameAsync(User user, string fullName)
    {
        var match = await FindMailUserAsync(user.Name, user.Domain);
        if (match == null)
        {
            _logger.LogInformation("Audit: change_fullname user={User} outcome=failure reason=account_not_found", user.Email);
            return Result.Failure($"User {user.Name}@{user.Domain} not found");
        }

        match.Value.MailUser.FullName = fullName;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Audit: change_fullname user={User} outcome=success", user.Email);
        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(User user, string newPassword, string oldPassword)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 8)
        {
            _logger.LogInformation("Audit: change_password user={User} outcome=failure reason=weak_password", user.Email);
            return Result.Failure($"Your password should contains 8 chars at least");
        }

        var match = await FindMailUserAsync(user.Name, user.Domain);
        if (match == null)
        {
            _logger.LogInformation("Audit: change_password user={User} outcome=failure reason=account_not_found", user.Email);
            return Result.Failure($"User {user.Name}@{user.Domain} not found");
        }

        MailUser mailUser = match.Value.MailUser;

        if (!PasswordMatches(mailUser, oldPassword))
        {
            _logger.LogInformation("Audit: change_password user={User} outcome=failure reason=bad_old_password", user.Email);
            return Result.Failure($"Invalid password");
        }

        mailUser.Password = newPassword;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Audit: change_password user={User} outcome=success", user.Email);
        return Result.Success();
    }

    /// <summary>
    /// Resolves the domain by name then the mailbox user within it (name matched
    /// case-insensitively). Returns null when either is missing.
    /// </summary>
    private async Task<(MailUser MailUser, MailDomain Domain)?> FindMailUserAsync(string name, string domainName)
    {
        MailDomain? domain = await _context.Domains.FirstOrDefaultAsync(dom => dom.Name == domainName);
        if (domain == null)
        {
            return null;
        }

        MailUser? mailUser = await _context.Users.FirstOrDefaultAsync(o => string.Equals(o.Name, name, StringComparison.InvariantCultureIgnoreCase) && o.DomainId == domain.Id);
        if (mailUser == null)
        {
            return null;
        }

        return (mailUser, domain);
    }

    private static bool PasswordMatches(MailUser mailUser, string password)
    {
        return Crypter.Sha512.Crypt(password, mailUser.Password) == mailUser.Password;
    }
}
