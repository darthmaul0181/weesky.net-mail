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
    public class UsersRepositoryTests
    {
        private const string TestEmail = "john@weesky.be";
        private const string TestPassword = "Password123!";

        private static (UsersRepository Repo, ApplicationDbContext Context) CreateSut()
        {
            var context = new TestDbContext(Guid.NewGuid().ToString());

            var domain = new MailDomain { Id = "WKY", Name = "weesky.be" };
            var otherDomain = new MailDomain { Id = "OTH", Name = "other.com" };
            var user = new MailUser
            {
                Name = "john",
                Password = Crypter.MD5.Crypt(TestPassword),
                DomainId = "WKY",
                Active = ActiveState.Y,
                FullName = "John Doe"
            };

            context.Domains.AddRange(domain, otherDomain);
            context.Users.Add(user);
            context.SaveChanges();

            var repo = new UsersRepository(context, Mock.Of<ILogger<UsersRepository>>());
            return (repo, context);
        }

        // --- FindByEmail ---

        [Fact]
        public void FindByEmail_WhenUserExists_ReturnsUser()
        {
            var (repo, _) = CreateSut();

            var user = repo.FindByEmail(TestEmail);

            Assert.NotNull(user);
            Assert.Equal("john", user.Name);
            Assert.Equal("weesky.be", user.Domain);
        }

        [Fact]
        public void FindByEmail_IsCaseInsensitiveForUsername()
        {
            var (repo, _) = CreateSut();

            var user = repo.FindByEmail("JOHN@weesky.be");

            Assert.NotNull(user);
        }

        [Fact]
        public void FindByEmail_WhenDomainNotFound_ReturnsNull()
        {
            var (repo, _) = CreateSut();

            var user = repo.FindByEmail("john@unknown-domain.com");

            Assert.Null(user);
        }

        [Fact]
        public void FindByEmail_WhenUsernameNotFound_ReturnsNull()
        {
            var (repo, _) = CreateSut();

            var user = repo.FindByEmail("nobody@weesky.be");

            Assert.Null(user);
        }

        [Theory]
        [InlineData("notanemail")]
        [InlineData("a@b@c")]
        public void FindByEmail_WithInvalidEmailFormat_ReturnsNull(string email)
        {
            var (repo, _) = CreateSut();

            var user = repo.FindByEmail(email);

            Assert.Null(user);
        }

        // --- IsValidPassword ---

        [Fact]
        public void IsValidPassword_WithCorrectPassword_ReturnsTrue()
        {
            var (repo, _) = CreateSut();
            var user = new User(TestEmail);

            Assert.True(repo.IsValidPassword(user, TestPassword));
        }

        [Fact]
        public void IsValidPassword_WithWrongPassword_ReturnsFalse()
        {
            var (repo, _) = CreateSut();
            var user = new User(TestEmail);

            Assert.False(repo.IsValidPassword(user, "WrongPassword!"));
        }

        [Fact]
        public void IsValidPassword_WhenDomainNotFound_ReturnsFalse()
        {
            var (repo, _) = CreateSut();
            var user = new User("john@nonexistent.com");

            Assert.False(repo.IsValidPassword(user, TestPassword));
        }

        [Fact]
        public void IsValidPassword_WhenUserNotFound_ReturnsFalse()
        {
            var (repo, _) = CreateSut();
            var user = new User("nobody@weesky.be");

            Assert.False(repo.IsValidPassword(user, TestPassword));
        }

        // --- GetAccountInfo ---

        [Fact]
        public void GetAccountInfo_WhenUserExists_ReturnsSuccess()
        {
            var (repo, _) = CreateSut();

            var result = repo.GetAccountInfo(new User(TestEmail));

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void GetAccountInfo_WhenUserExists_ReturnsCorrectInfo()
        {
            var (repo, _) = CreateSut();

            var result = repo.GetAccountInfo(new User(TestEmail));

            Assert.Equal("john", result.Value.UserName);
            Assert.Equal("John Doe", result.Value.FullName);
            Assert.Equal("WKY", result.Value.Mailbox);
        }

        [Fact]
        public void GetAccountInfo_WhenNoDomainOwnerships_ReturnsPrimaryDomainInList()
        {
            var (repo, _) = CreateSut();

            var result = repo.GetAccountInfo(new User(TestEmail));

            Assert.Single(result.Value.Domains);
            Assert.Contains(result.Value.Domains, d => d.Name == "weesky.be");
        }

        [Fact]
        public void GetAccountInfo_WithOwnedDomains_ReturnsAllOwnedDomains()
        {
            var (repo, context) = CreateSut();
            var userId = context.Users.First(u => u.Name == "john").Id;
            context.DomainsOwnerships.Add(new MailDomainOwnership { DomainId = "OTH", UserId = userId });
            context.SaveChanges();

            var result = repo.GetAccountInfo(new User(TestEmail));

            Assert.Contains(result.Value.Domains, d => d.Name == "other.com");
        }

        [Fact]
        public void GetAccountInfo_WhenDomainNotFound_ReturnsFailure()
        {
            var (repo, _) = CreateSut();

            var result = repo.GetAccountInfo(new User("john@nonexistent.com"));

            Assert.True(result.IsFailure);
        }

        [Fact]
        public void GetAccountInfo_WhenUserNotFound_ReturnsFailure()
        {
            var (repo, _) = CreateSut();

            var result = repo.GetAccountInfo(new User("nobody@weesky.be"));

            Assert.True(result.IsFailure);
        }

        // --- ChangePassword ---

        [Fact]
        public void ChangePassword_WithWeakPassword_ReturnsFailure()
        {
            var (repo, _) = CreateSut();

            var result = repo.ChangePassword(new User(TestEmail), "short", TestPassword);

            Assert.True(result.IsFailure);
        }

        [Fact]
        public void ChangePassword_WithWrongOldPassword_ReturnsFailure()
        {
            var (repo, _) = CreateSut();

            var result = repo.ChangePassword(new User(TestEmail), "NewPassword123!", "WrongOldPassword");

            Assert.True(result.IsFailure);
        }

        [Fact]
        public void ChangePassword_WhenUserNotFound_ReturnsFailure()
        {
            var (repo, _) = CreateSut();

            var result = repo.ChangePassword(new User("nobody@weesky.be"), "NewPassword123!", TestPassword);

            Assert.True(result.IsFailure);
        }

        [Fact]
        public void ChangePassword_WithValidData_ReturnsSuccess()
        {
            var (repo, _) = CreateSut();

            var result = repo.ChangePassword(new User(TestEmail), "NewPassword123!", TestPassword);

            Assert.True(result.IsSuccess);
        }

        [Theory]
        [InlineData("1234567")]
        [InlineData("")]
        public void ChangePassword_WithPasswordTooShort_ReturnsFailure(string newPassword)
        {
            var (repo, _) = CreateSut();

            var result = repo.ChangePassword(new User(TestEmail), newPassword, TestPassword);

            Assert.True(result.IsFailure);
        }

        [Fact]
        public void ChangePassword_WhenDomainNotFound_ReturnsFailure()
        {
            var (repo, _) = CreateSut();

            var result = repo.ChangePassword(new User("john@nonexistent.com"), "NewPassword123!", TestPassword);

            Assert.True(result.IsFailure);
        }

        // --- ChangeFullName ---

        [Fact]
        public void ChangeFullName_WithValidUser_ReturnsSuccess()
        {
            var (repo, _) = CreateSut();

            var result = repo.ChangeFullName(new User(TestEmail), "New Name");

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void ChangeFullName_WithValidUser_PersistsNewName()
        {
            var (repo, context) = CreateSut();

            repo.ChangeFullName(new User(TestEmail), "New Name");

            Assert.Equal("New Name", context.Users.First(u => u.Name == "john").FullName);
        }

        [Fact]
        public void ChangeFullName_WhenDomainNotFound_ReturnsFailure()
        {
            var (repo, _) = CreateSut();

            var result = repo.ChangeFullName(new User("john@nonexistent.com"), "New Name");

            Assert.True(result.IsFailure);
        }

        [Fact]
        public void ChangeFullName_WhenUserNotFound_ReturnsFailure()
        {
            var (repo, _) = CreateSut();

            var result = repo.ChangeFullName(new User("nobody@weesky.be"), "New Name");

            Assert.True(result.IsFailure);
        }
    }
}
