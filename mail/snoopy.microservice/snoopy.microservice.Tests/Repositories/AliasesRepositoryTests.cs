using CryptSharp.Core;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories
{
    public class AliasesRepositoryTests
    {
        private const string PrimaryDomain = "weesky.be";
        private const string OwnedDomain = "other.com";
        private const string UnownedDomain = "stranger.com";

        private static (AliasesRepository Repo, ApplicationDbContext Context, int UserId) CreateSut()
        {
            var context = new TestDbContext(Guid.NewGuid().ToString());

            context.Domains.AddRange(
                new MailDomain { Id = "WKY", Name = PrimaryDomain },
                new MailDomain { Id = "OTH", Name = OwnedDomain },
                new MailDomain { Id = "STR", Name = UnownedDomain }
            );
            var user = new MailUser
            {
                Name = "john",
                Password = Crypter.MD5.Crypt("password"),
                DomainId = "WKY",
                Active = ActiveState.Y,
                FullName = "John Doe"
            };
            context.Users.Add(user);
            context.SaveChanges();

            var userId = context.Users.First(u => u.Name == "john").Id;
            context.DomainsOwnerships.Add(new MailDomainOwnership { DomainId = "OTH", UserId = userId });
            context.SaveChanges();

            var repo = new AliasesRepository(context, Mock.Of<ILogger<AliasesRepository>>());
            return (repo, context, userId);
        }

        private static User AuthUser => new("john@" + PrimaryDomain);

        // --- UserOwnsDomain ---

        [Fact]
        public void UserOwnsDomain_WithPrimaryDomain_ReturnsTrue()
        {
            var (repo, _, _) = CreateSut();

            Assert.True(repo.UserOwnsDomain(AuthUser, PrimaryDomain));
        }

        [Fact]
        public void UserOwnsDomain_WithOwnedDomain_ReturnsTrue()
        {
            var (repo, _, _) = CreateSut();

            Assert.True(repo.UserOwnsDomain(AuthUser, OwnedDomain));
        }

        [Fact]
        public void UserOwnsDomain_WithUnownedDomain_ReturnsFalse()
        {
            var (repo, _, _) = CreateSut();

            Assert.False(repo.UserOwnsDomain(AuthUser, UnownedDomain));
        }

        [Fact]
        public void UserOwnsDomain_WhenUserNotFound_ReturnsFalse()
        {
            var (repo, _, _) = CreateSut();

            Assert.False(repo.UserOwnsDomain(new User("nobody@" + PrimaryDomain), OwnedDomain));
        }

        // --- AddAlias ---

        [Fact]
        public void AddAlias_WithPrimaryDomain_ReturnsSuccess()
        {
            var (repo, _, _) = CreateSut();

            var result = repo.AddAlias(AuthUser, new Alias { Name = "johnny", Domain = PrimaryDomain });

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void AddAlias_WithOwnedDomain_ReturnsSuccess()
        {
            var (repo, _, _) = CreateSut();

            var result = repo.AddAlias(AuthUser, new Alias { Name = "johnny", Domain = OwnedDomain });

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void AddAlias_WithUnownedDomain_ReturnsFailure()
        {
            var (repo, _, _) = CreateSut();

            var result = repo.AddAlias(AuthUser, new Alias { Name = "johnny", Domain = UnownedDomain });

            Assert.True(result.IsFailure);
        }

        [Fact]
        public void AddAlias_WhenAliasAlreadyExists_ReturnsFailure()
        {
            var (repo, _, _) = CreateSut();
            repo.AddAlias(AuthUser, new Alias { Name = "duplicate", Domain = PrimaryDomain });

            var result = repo.AddAlias(AuthUser, new Alias { Name = "duplicate", Domain = PrimaryDomain });

            Assert.True(result.IsFailure);
        }

        [Fact]
        public void AddAlias_DuplicateCheckIsCaseInsensitive()
        {
            var (repo, _, _) = CreateSut();
            repo.AddAlias(AuthUser, new Alias { Name = "Duplicate", Domain = PrimaryDomain });

            var result = repo.AddAlias(AuthUser, new Alias { Name = "duplicate", Domain = PrimaryDomain });

            Assert.True(result.IsFailure);
        }

        [Fact]
        public void AddAlias_WithNullAlias_ThrowsArgumentNullException()
        {
            var (repo, _, _) = CreateSut();

            Assert.Throws<ArgumentNullException>(() => repo.AddAlias(AuthUser, null!));
        }

        [Fact]
        public void AddAlias_PersistsAliasToDatabase()
        {
            var (repo, context, userId) = CreateSut();

            repo.AddAlias(AuthUser, new Alias { Name = "newone", Domain = PrimaryDomain });

            Assert.True(context.Aliases.Any(a => a.Name == "newone" && a.DestinationUserId == userId));
        }

        // --- DeleteAlias ---

        [Fact]
        public void DeleteAlias_WhenAliasExists_ReturnsSuccess()
        {
            var (repo, _, _) = CreateSut();
            repo.AddAlias(AuthUser, new Alias { Name = "todelete", Domain = PrimaryDomain });

            var result = repo.DeleteAlias(AuthUser, new Alias { Name = "todelete", Domain = PrimaryDomain });

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void DeleteAlias_WhenAliasNotFound_ReturnsFailure()
        {
            var (repo, _, _) = CreateSut();

            var result = repo.DeleteAlias(AuthUser, new Alias { Name = "nonexistent", Domain = PrimaryDomain });

            Assert.True(result.IsFailure);
        }

        [Fact]
        public void DeleteAlias_WithUnownedDomain_ReturnsFailure()
        {
            var (repo, _, _) = CreateSut();

            var result = repo.DeleteAlias(AuthUser, new Alias { Name = "alias", Domain = UnownedDomain });

            Assert.True(result.IsFailure);
        }

        [Fact]
        public void DeleteAlias_WithNullAlias_ThrowsArgumentNullException()
        {
            var (repo, _, _) = CreateSut();

            Assert.Throws<ArgumentNullException>(() => repo.DeleteAlias(AuthUser, null!));
        }

        [Fact]
        public void DeleteAlias_RemovesAliasFromDatabase()
        {
            var (repo, context, userId) = CreateSut();
            repo.AddAlias(AuthUser, new Alias { Name = "gone", Domain = PrimaryDomain });

            repo.DeleteAlias(AuthUser, new Alias { Name = "gone", Domain = PrimaryDomain });

            Assert.False(context.Aliases.Any(a => a.Name == "gone" && a.DestinationUserId == userId));
        }

        // --- GetAliases ---

        [Fact]
        public void GetAliases_WithNoAliases_ReturnsEmptyList()
        {
            var (repo, _, _) = CreateSut();

            var aliases = repo.GetAliases(AuthUser).ToList();

            Assert.Empty(aliases);
        }

        [Fact]
        public void GetAliases_ReturnsPrimaryDomainAliases()
        {
            var (repo, _, _) = CreateSut();
            repo.AddAlias(AuthUser, new Alias { Name = "alias1", Domain = PrimaryDomain });

            var aliases = repo.GetAliases(AuthUser).ToList();

            Assert.Contains(aliases, a => a.Name == "alias1" && a.Domain == PrimaryDomain);
        }

        [Fact]
        public void GetAliases_ReturnsOwnedDomainAliases()
        {
            var (repo, _, _) = CreateSut();
            repo.AddAlias(AuthUser, new Alias { Name = "alias2", Domain = OwnedDomain });

            var aliases = repo.GetAliases(AuthUser).ToList();

            Assert.Contains(aliases, a => a.Name == "alias2" && a.Domain == OwnedDomain);
        }

        [Fact]
        public void GetAliases_DoesNotReturnAliasesOfOtherUsers()
        {
            var (repo, context, _) = CreateSut();
            var otherUser = new MailUser { Name = "alice", Password = "x", DomainId = "WKY", Active = ActiveState.Y, FullName = "Alice Smith" };
            context.Users.Add(otherUser);
            context.SaveChanges();
            var otherUserId = context.Users.First(u => u.Name == "alice").Id;
            context.Aliases.Add(new MailAlias { Name = "alice-alias", Domain = "WKY", DestinationUserId = otherUserId });
            context.SaveChanges();

            var aliases = repo.GetAliases(AuthUser).ToList();

            Assert.DoesNotContain(aliases, a => a.Name == "alice-alias");
        }
    }
}
