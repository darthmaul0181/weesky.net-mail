using weesky.MailAdminRestAPI.Data;
using weesky.MailAdminRestAPI.Models;
using weesky.MailAdminRestAPI.Services;

namespace weesky.MailAdminRestAPI.Repositories
{
	public class AliasesRepository : IAliasesRepository
	{
		private readonly ApplicationDbContext _context;

		public AliasesRepository(ApplicationDbContext dbContext)
		{
			_context = dbContext;
		}

		public IEnumerable<Alias> GetAliases(User user)
		{
			var aliases = from alias in _context.Aliases
						  from usr in _context.Users
						  from domain in _context.Domains
						  where alias.DestinationUserId == usr.Id
						       && usr.DomainId == domain.Id
							   && usr.Name == user.Name
							   && domain.Name == user.DomainId
							   && (alias.Domain == usr.DomainId || _context.DomainsOwnerships.Any(own => own.DomainId == alias.Domain && own.UserId == usr.Id))
						  select new Alias
						  {
							  Name = alias.Name,
							  Domain = _context.Domains.Single(d => d.Id == alias.Domain).Name
						  };

			return aliases;
		}

		public ResultEnveloppe AddAlias(User user, Alias alias)
		{
			if(alias == null)
				throw new ArgumentNullException("alias");

			if(!UserOwnsDomain(user, alias.Domain))
			{
				return ResultEnveloppe.CrateErrorEnveloppe($"User '{user.Name}@{user.DomainId} doesn't own domain '{alias.Domain}'");
			}

			MailUser mailUser = GetMailUserBy(user);
			MailDomain mailDomain = GetDomainBy(alias.Domain);

			if(_context.Aliases.Any(a => string.Equals(a.Name, alias.Name, StringComparison.InvariantCultureIgnoreCase) && string.Equals(mailDomain.Id, a.Domain, StringComparison.InvariantCultureIgnoreCase) && a.DestinationUserId == mailUser.Id))
			{
				return ResultEnveloppe.CrateErrorEnveloppe($"Alias '{alias.Name}@{alias.Domain}' already exists for this user");
			}

			_context.Aliases.Add(new MailAlias
			{
				Name = alias.Name,
				Domain = mailDomain.Id,
				DestinationUserId = mailUser.Id,
			});

			_context.SaveChanges();

			return ResultEnveloppe.CreateSuccessEnveloppe();
		}

		public ResultEnveloppe DeleteAlias(User user, Alias alias)
		{
			if(alias == null)
				throw new ArgumentNullException(nameof(alias));

			if (!UserOwnsDomain(user, alias.Domain))
			{
				return ResultEnveloppe.CrateErrorEnveloppe($"User '{user.Name}@{user.DomainId} doesn't own domain '{alias.Domain}'");
			}

			MailUser mailUser = GetMailUserBy(user);
			MailDomain mailDomain = GetDomainBy(alias.Domain);

			MailAlias mailAlias = _context.Aliases.FirstOrDefault(a => string.Equals(a.Name, alias.Name, StringComparison.InvariantCultureIgnoreCase) && string.Equals(mailDomain.Id, a.Domain, StringComparison.InvariantCultureIgnoreCase) && a.DestinationUserId == mailUser.Id);
			if(mailAlias == null)
			{
				return ResultEnveloppe.CrateErrorEnveloppe($"Alias '{alias.Name}@{alias.Domain} not found");
			}

			_context.Aliases.Remove(mailAlias);
			_context.SaveChanges();

			return ResultEnveloppe.CreateSuccessEnveloppe();
		}

		public bool UserOwnsDomain(User user, string domainName)
		{
			if(string.Equals(user.DomainId, domainName))
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
							&& string.Equals(domain.Name, user.DomainId, StringComparison.InvariantCultureIgnoreCase)
						select usr;

			return query.First();
		}

		private MailDomain GetDomainBy(string name)
		{
			return _context.Domains.FirstOrDefault(domain => string.Equals(domain.Name, name, StringComparison.InvariantCultureIgnoreCase));
		}
	}
}
