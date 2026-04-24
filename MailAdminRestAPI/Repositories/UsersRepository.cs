using CryptSharp.Core;
using CSharpFunctionalExtensions;
using weesky.MailAdminRestAPI.Data;
using weesky.MailAdminRestAPI.Models;

namespace weesky.MailAdminRestAPI.Repositories
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

			MailDomain domain = _context.Domains.FirstOrDefault(dom => dom.Name == emailParts[1]);
			if (domain == null)
			{
				return null;
			}

			MailUser user = _context.Users.FirstOrDefault(o => string.Equals(o.Name, emailParts[0], StringComparison.InvariantCultureIgnoreCase) && o.DomainId == domain.Id);
			if (user == null)
			{
				return null;
			}

			return new User($"{user.Name}@{domain.Name}");
		}

		public bool IsValidPassword(User user, string password)
		{
			MailDomain domain = _context.Domains.FirstOrDefault(dom => dom.Name == user.Domain);
			if (domain == null)
			{
				return false;
			}

			MailUser mailUser = _context.Users.FirstOrDefault(o => string.Equals(o.Name, user.Name, StringComparison.InvariantCultureIgnoreCase) && o.DomainId == domain.Id);
			if (mailUser == null)
			{
				return false;
			}

			return Crypter.CheckPassword(password, mailUser.Password);
		}

		public Result<AccountInfo> GetAccountInfo(User user)
		{
			MailDomain domain = _context.Domains.FirstOrDefault(d => d.Name == user.Domain);
			if (domain == null)
				return Result.Failure<AccountInfo>("Account not found");

			MailUser mailUser = _context.Users.FirstOrDefault(u =>
				string.Equals(u.Name, user.Name, StringComparison.InvariantCultureIgnoreCase) &&
				u.DomainId == domain.Id);
			if (mailUser == null)
				return Result.Failure<AccountInfo>("Account not found");

			var ownedDomains = _context.DomainsOwnerships
				.Where(o => o.UserId == mailUser.Id)
				.Join(_context.Domains, o => o.DomainId, d => d.Id, (o, d) => new Domain { Id = d.Id, Name = d.Name })
				.ToList();

			if (ownedDomains.Count == 0)
				ownedDomains.Add(new Domain { Id = domain.Id, Name = domain.Name });

			return Result.Success(new AccountInfo
			{
				UserId = mailUser.Id,
				UserName = mailUser.Name,
				FullName = mailUser.FullName,
				Mailbox = mailUser.DomainId,
				Domains = ownedDomains
			});
		}

		public Result ChangePassword(User user, string newPassword, string oldPassword)
		{
			if(string.IsNullOrEmpty(newPassword) || newPassword.Length < 8)
			{
				_logger.LogInformation("Audit: change_password user={User} outcome=failure reason=weak_password", user.Email);
				return Result.Failure($"Your password should contains 8 chars at least");
			}

			MailDomain domain = _context.Domains.FirstOrDefault(dom => dom.Name == user.Domain);
			if (domain == null)
			{
				_logger.LogInformation("Audit: change_password user={User} outcome=failure reason=account_not_found", user.Email);
				return Result.Failure($"User {user.Name}@{user.Domain} not found");
			}

			MailUser mailUser = _context.Users.FirstOrDefault(o => string.Equals(o.Name, user.Name, StringComparison.InvariantCultureIgnoreCase) && o.DomainId == domain.Id);
			if (mailUser == null)
			{
				_logger.LogInformation("Audit: change_password user={User} outcome=failure reason=account_not_found", user.Email);
				return Result.Failure($"User {user.Name}@{user.Domain} not found");
			}

			if(!Crypter.CheckPassword(oldPassword, mailUser.Password))
			{
				_logger.LogInformation("Audit: change_password user={User} outcome=failure reason=bad_old_password", user.Email);
				return Result.Failure($"Invalid password");
			}

			mailUser.Password = newPassword;
			_context.SaveChanges();

			_logger.LogInformation("Audit: change_password user={User} outcome=success", user.Email);
			return Result.Success();
		}
	}
}
