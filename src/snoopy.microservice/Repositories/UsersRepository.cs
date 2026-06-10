using CryptSharp.Core;
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Repositories
{
	public class UsersRepository : IUsersRepository
	{
		private readonly ApplicationDbContext _context;
		private readonly ILogger<UsersRepository> _logger;

		public UsersRepository(ApplicationDbContext dbContext, ILogger<UsersRepository> logger)
		{
			_context = dbContext;
			_logger = logger;
		}

		public User FindByEmail(string email)
		{
			string[] emailParts = email.Split(new char[] { '@' });

			if (emailParts == null || emailParts.Length != 2)
			{
				return null;
			}

			var match = FindMailUser(emailParts[0], emailParts[1]);
			if (match == null)
			{
				return null;
			}

			return new User($"{match.Value.MailUser.Name}@{match.Value.Domain.Name}");
		}

		public bool IsValidPassword(User user, string password)
		{
			var match = FindMailUser(user.Name, user.Domain);
			return match != null && PasswordMatches(match.Value.MailUser, password);
		}

		public Result<AccountInfo> GetAccountInfo(User user)
		{
			var match = FindMailUser(user.Name, user.Domain);
			if (match == null)
				return Result.Failure<AccountInfo>("Account not found");

			var (mailUser, domain) = match.Value;

			var ownedDomains = _context.DomainsOwnerships
				.Where(o => o.UserId == mailUser.Id)
				.Join(_context.Domains, o => o.DomainId, d => d.Id, (o, d) => new Domain { Id = d.Id, Name = d.Name })
				.ToList();

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

		public Result ChangeFullName(User user, string fullName)
		{
			var match = FindMailUser(user.Name, user.Domain);
			if (match == null)
			{
				_logger.LogInformation("Audit: change_fullname user={User} outcome=failure reason=account_not_found", user.Email);
				return Result.Failure($"User {user.Name}@{user.Domain} not found");
			}

			match.Value.MailUser.FullName = fullName;
			_context.SaveChanges();

			_logger.LogInformation("Audit: change_fullname user={User} outcome=success", user.Email);
			return Result.Success();
		}

		public Result ChangePassword(User user, string newPassword, string oldPassword)
		{
			if(string.IsNullOrEmpty(newPassword) || newPassword.Length < 8)
			{
				_logger.LogInformation("Audit: change_password user={User} outcome=failure reason=weak_password", user.Email);
				return Result.Failure($"Your password should contains 8 chars at least");
			}

			var match = FindMailUser(user.Name, user.Domain);
			if (match == null)
			{
				_logger.LogInformation("Audit: change_password user={User} outcome=failure reason=account_not_found", user.Email);
				return Result.Failure($"User {user.Name}@{user.Domain} not found");
			}

			MailUser mailUser = match.Value.MailUser;

			if(!PasswordMatches(mailUser, oldPassword))
			{
				_logger.LogInformation("Audit: change_password user={User} outcome=failure reason=bad_old_password", user.Email);
				return Result.Failure($"Invalid password");
			}

			mailUser.Password = newPassword;
			_context.SaveChanges();

			_logger.LogInformation("Audit: change_password user={User} outcome=success", user.Email);
			return Result.Success();
		}

		/// <summary>
		/// Resolves the domain by name then the mailbox user within it (name matched
		/// case-insensitively). Returns null when either is missing.
		/// </summary>
		private (MailUser MailUser, MailDomain Domain)? FindMailUser(string name, string domainName)
		{
			MailDomain domain = _context.Domains.FirstOrDefault(dom => dom.Name == domainName);
			if (domain == null)
			{
				return null;
			}

			MailUser mailUser = _context.Users.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.InvariantCultureIgnoreCase) && o.DomainId == domain.Id);
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
}
