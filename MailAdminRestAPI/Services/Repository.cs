using weesky.MailAdminRestAPI.Data;
using weesky.MailAdminRestAPI.Models;
using CryptSharp.Core;

namespace weesky.MailAdminRestAPI.Services
{
	public class Repository : IRepository
	{
		private readonly ApplicationDbContext _context;

		public Repository(ApplicationDbContext dbContext)
		{
			_context = dbContext;
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

			MailUser malUser = _context.Users.FirstOrDefault(o => string.Equals(o.Name, user.Name, StringComparison.InvariantCultureIgnoreCase) && o.DomainId == domain.Id);
			if (user == null)
			{
				return false;
			}

			return Crypter.CheckPassword(password, malUser.Password);
		}

		public IEnumerable<Alias> GetAliases(User user)
		{
			var aliases = from alias in _context.Aliases
						  from usr in _context.Users
						  from domain in _context.Domains
						  where alias.DestinationUserId == usr.Id
						       && usr.DomainId == domain.Id
							   && usr.Name == user.Name
							   && domain.Name == user.Domain
							   && (alias.SourceDomainId == usr.DomainId || _context.DomainsOwnerships.Any(own => own.DomainId == alias.SourceDomainId && own.UserId == usr.Id))
						  select new Alias
						  {
							  Name = alias.SourceName,
							  Domain = _context.Domains.Single(d => d.Id == alias.SourceDomainId).Name
						  };

			return aliases;
		}

		public RepositoryResult AddAlias(User user, Alias alias)
		{
			if(alias == null)
				throw new ArgumentNullException("alias");

			if(!UserOwnsDomain(user, alias.Domain))
			{
				return new RepositoryResult
				{
					State = RespositoryResultState.Error,
					Message = $"User '{user.Name}@{user.Domain} doesn't own domain '{alias.Domain}'"
				};
			}

			MailUser mailUser = GetMailUserBy(user);
			MailDomain mailDomain = GetDomainBy(alias.Domain);

			if(_context.Aliases.Any(a => string.Equals(a.SourceName, alias.Name, StringComparison.InvariantCultureIgnoreCase) && string.Equals(mailDomain.Id, a.SourceDomainId, StringComparison.InvariantCultureIgnoreCase) && a.DestinationUserId == mailUser.Id))
			{
				return new RepositoryResult
				{
					State = RespositoryResultState.Error,
					Message = $"Alias '{alias.Name}@{alias.Domain}' already exists for this user"
				};
			}

			_context.Aliases.Add(new MailAlias
			{
				SourceName = alias.Name,
				SourceDomainId = mailDomain.Id,
				DestinationUserId = mailUser.Id,
			});

			_context.SaveChanges();

			return new RepositoryResult();
		}

		public RepositoryResult DeleteAlias(User user, Alias alias)
		{
			if(alias == null)
				throw new ArgumentNullException(nameof(alias));

			if (!UserOwnsDomain(user, alias.Domain))
			{
				return new RepositoryResult
				{
					State = RespositoryResultState.Error,
					Message = $"User '{user.Name}@{user.Domain} doesn't own domain '{alias.Domain}'"
				};
			}

			MailUser mailUser = GetMailUserBy(user);
			MailDomain mailDomain = GetDomainBy(alias.Domain);

			MailAlias mailAlias = _context.Aliases.FirstOrDefault(a => string.Equals(a.SourceName, alias.Name, StringComparison.InvariantCultureIgnoreCase) && string.Equals(mailDomain.Id, a.SourceDomainId, StringComparison.InvariantCultureIgnoreCase) && a.DestinationUserId == mailUser.Id);
			if(mailAlias == null)
			{
				return new RepositoryResult
				{
					State = RespositoryResultState.Error,
					Message = $"Alias '{alias.Name}@{alias.Domain} not found"
				};
			}

			_context.Aliases.Remove(mailAlias);
			_context.SaveChanges();

			return new RepositoryResult();
		}

		public bool UserOwnsDomain(User user, string domainName)
		{
			if(string.Equals(user.Domain, domainName))
			{
				return true;
			}
			
			MailUser mailUser = GetMailUserBy(user);
			return _context.DomainsOwnerships.Any(ownedDomain => ownedDomain.UserId == mailUser.Id && _context.Domains.Any(domain => string.Equals(domain.Id, ownedDomain.DomainId, StringComparison.InvariantCultureIgnoreCase)));
		}

		private MailUser GetMailUserBy(User user)
		{
			var query = from usr in _context.Users
						from domain in _context.Domains
						where usr.DomainId == domain.Id
							&& string.Equals(usr.Name, user.Name, StringComparison.InvariantCultureIgnoreCase)
							&& string.Equals(domain.Name, user.Domain, StringComparison.InvariantCultureIgnoreCase)
						select usr;

			return query.First();
		}

		private MailDomain GetDomainBy(string name)
		{
			return _context.Domains.FirstOrDefault(domain => string.Equals(domain.Name, name, StringComparison.InvariantCultureIgnoreCase));
		}
	}
}
