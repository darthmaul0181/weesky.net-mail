using System.Security.Cryptography;
using System.Text;
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

    /// <summary>
    /// Checks an email and password in one pass, in time that does not depend on whether the
    /// mailbox exists.
    /// </summary>
    /// <remarks>
    /// The SHA-512 crypt is deliberately slow, and it used to run only once a mailbox had been
    /// found: an unknown address answered in a fraction of the time a wrong password did, so the
    /// login endpoint told anyone who measured it which addresses are real. The hash therefore
    /// runs on every call, against <see cref="AbsentAccountHash"/> when there is nothing to
    /// compare with, and the verdict is formed only once both the lookup and the hash are done.
    ///
    /// This holds only while the stored hashes and the decoy share their cost parameters. Both
    /// come from CryptSharp's default <c>$6$</c> rounds, the same the MariaDB trigger writes.
    /// </remarks>
    public async Task<CredentialCheck> VerifyCredentialsAsync(string email, string password)
    {
        var parts = email.Split('@');
        var match = parts.Length == 2 ? await FindMailUserAsync(parts[0], parts[1]) : null;

        var storedHash = match?.MailUser.Password ?? AbsentAccountHash;
        var passwordMatches = PasswordMatches(storedHash, password);

        if (match is null) return CredentialCheck.Failed(CredentialResult.UnknownAccount);
        if (match.Value.MailUser.Active != ActiveState.Y) return CredentialCheck.Failed(CredentialResult.Deactivated);

        if (!passwordMatches)
        {
            WarnIfUnverifiable(email, storedHash);
            return CredentialCheck.Failed(CredentialResult.WrongPassword);
        }

        return CredentialCheck.Success(new User($"{match.Value.MailUser.Name}@{match.Value.Domain.Name}"));
    }

    /// <summary>
    /// The hash a login falls back on when no mailbox matches, so the crypt still runs and still
    /// costs what a real one costs. Computed once: generating it per call would itself be work an
    /// existing account does not do.
    /// </summary>
    internal static readonly string AbsentAccountHash =
        Crypter.Sha512.Crypt("no-such-account", Crypter.Sha512.GenerateSalt());

    /// <summary>
    /// A stored value that is not a <c>$6$</c> crypt hash cannot be verified at all: CryptSharp
    /// reads the salt out of whatever it is handed, so anything else yields a digest that can
    /// never match, and the account reads as "wrong password" forever.
    ///
    /// Nothing else about the failure distinguishes that from a genuinely wrong password, which
    /// is exactly why it is worth a line of its own. It names the scheme, never the hash.
    /// </summary>
    private void WarnIfUnverifiable(string email, string storedHash)
    {
        if (storedHash.StartsWith("$6$", StringComparison.Ordinal)) return;

        _logger.LogWarning(
            "Audit: login email={Email} outcome=failure reason=unverifiable_hash scheme={Scheme}. " +
            "The stored password is not a $6$ SHA-512 crypt hash, so no password can match it. " +
            "Check the MariaDB INSERT_PASSWORD/UPDATE_PASSWORD trigger on the users table.",
            email, HashScheme(storedHash));
    }

    /// <summary>
    /// The scheme marker of a stored password, for diagnostics. Deliberately an allow-list rather
    /// than "the text up to the second separator": the one case worth naming is a column holding
    /// something that is not a hash at all, and that something must not reach the log.
    /// </summary>
    internal static string HashScheme(string? storedHash) => storedHash switch
    {
        null or "" => "(empty)",
        _ when storedHash.StartsWith("$6$", StringComparison.Ordinal) => "$6$ (sha512-crypt)",
        _ when storedHash.StartsWith("$5$", StringComparison.Ordinal) => "$5$ (sha256-crypt)",
        _ when storedHash.StartsWith("$1$", StringComparison.Ordinal) => "$1$ (md5-crypt)",
        _ when storedHash.StartsWith("$2", StringComparison.Ordinal) => "$2 (bcrypt)",
        _ when storedHash.StartsWith('{') => BracketedScheme(storedHash),
        _ => "(unrecognised)"
    };

    /// <summary>A Dovecot-style <c>{SCHEME}</c> prefix, or nothing recognisable.</summary>
    private static string BracketedScheme(string storedHash)
    {
        var close = storedHash.IndexOf('}');
        if (close <= 1) return "(unrecognised)";

        var scheme = storedHash[1..close];
        return scheme.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_')
            ? $"{{{scheme}}}"
            : "(unrecognised)";
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

        if (!PasswordMatches(mailUser.Password, oldPassword))
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
    /// The mailbox and its domain in one round trip, the user name matched case-insensitively.
    /// Null when there is no such mailbox.
    /// </summary>
    /// <remarks>
    /// One joined query rather than the two sequential reads this replaced — domain, then user.
    /// Beyond the saved round trip that halves the cost of every login, a single query is what
    /// makes an unknown domain cost the same as an unknown mailbox: with two, the first returning
    /// nothing skipped the second, and the response time said which of the two it was.
    /// </remarks>
    private async Task<(MailUser MailUser, MailDomain Domain)?> FindMailUserAsync(string name, string domainName)
    {
        var row = await (from mailUser in _context.Users
                         join domain in _context.Domains on mailUser.DomainId equals domain.Id
                         where domain.Name == domainName
                            && string.Equals(mailUser.Name, name, StringComparison.InvariantCultureIgnoreCase)
                         select new { MailUser = mailUser, Domain = domain })
            .FirstOrDefaultAsync();

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
