using CryptSharp.Core;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class UsersRepositoryTests
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
            Password = Crypter.Sha512.Crypt(TestPassword, Crypter.Sha512.GenerateSalt()),
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
    public async Task FindByEmail_WhenUserExists_ReturnsUser()
    {
        var (repo, _) = CreateSut();

        var user = await repo.FindByEmailAsync(TestEmail);

        Assert.NotNull(user);
        Assert.Equal("john", user.Name);
        Assert.Equal("weesky.be", user.Domain);
    }

    [Fact]
    public async Task FindByEmail_IsCaseInsensitiveForUsername()
    {
        var (repo, _) = CreateSut();

        var user = await repo.FindByEmailAsync("JOHN@weesky.be");

        Assert.NotNull(user);
    }

    [Fact]
    public async Task FindByEmail_WhenDomainNotFound_ReturnsNull()
    {
        var (repo, _) = CreateSut();

        var user = await repo.FindByEmailAsync("john@unknown-domain.com");

        Assert.Null(user);
    }

    // Dovecot refuses IMAP for a deactivated mailbox, but everything that does not go through
    // the mail server -- aliases, preferences, admin, and Sieve rules, which authenticate as the
    // master user -- kept working until this filter existed.
    [Fact]
    public async Task FindByEmail_WhenAccountIsDeactivated_ReturnsNull()
    {
        var (repo, context) = CreateSut();
        context.Users.First().Active = ActiveState.N;
        await context.SaveChangesAsync();

        var user = await repo.FindByEmailAsync(TestEmail);

        Assert.Null(user);
    }

    [Fact]
    public async Task FindByEmail_WhenUsernameNotFound_ReturnsNull()
    {
        var (repo, _) = CreateSut();

        var user = await repo.FindByEmailAsync("nobody@weesky.be");

        Assert.Null(user);
    }

    [Theory]
    [InlineData("notanemail")]
    [InlineData("a@b@c")]
    public async Task FindByEmail_WithInvalidEmailFormat_ReturnsNull(string email)
    {
        var (repo, _) = CreateSut();

        var user = await repo.FindByEmailAsync(email);

        Assert.Null(user);
    }

    // --- IsValidPassword ---

    [Fact]
    public async Task IsValidPassword_WithCorrectPassword_ReturnsTrue()
    {
        var (repo, _) = CreateSut();
        var user = new User(TestEmail);

        Assert.True(await repo.IsValidPasswordAsync(user, TestPassword));
    }

    [Fact]
    public async Task IsValidPassword_WithWrongPassword_ReturnsFalse()
    {
        var (repo, _) = CreateSut();
        var user = new User(TestEmail);

        Assert.False(await repo.IsValidPasswordAsync(user, "WrongPassword!"));
    }

    [Fact]
    public async Task IsValidPassword_WhenDomainNotFound_ReturnsFalse()
    {
        var (repo, _) = CreateSut();
        var user = new User("john@nonexistent.com");

        Assert.False(await repo.IsValidPasswordAsync(user, TestPassword));
    }

    [Fact]
    public async Task IsValidPassword_WhenUserNotFound_ReturnsFalse()
    {
        var (repo, _) = CreateSut();
        var user = new User("nobody@weesky.be");

        Assert.False(await repo.IsValidPasswordAsync(user, TestPassword));
    }

    // --- GetAccountInfo ---

    [Fact]
    public async Task GetAccountInfo_WhenUserExists_ReturnsSuccess()
    {
        var (repo, _) = CreateSut();

        var result = await repo.GetAccountInfoAsync(new User(TestEmail));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetAccountInfo_WhenUserExists_ReturnsCorrectInfo()
    {
        var (repo, _) = CreateSut();

        var result = await repo.GetAccountInfoAsync(new User(TestEmail));

        Assert.Equal("john", result.Value.UserName);
        Assert.Equal("John Doe", result.Value.FullName);
        Assert.Equal("WKY", result.Value.Mailbox);
    }

    [Fact]
    public async Task GetAccountInfo_WhenNoDomainOwnerships_ReturnsPrimaryDomainInList()
    {
        var (repo, _) = CreateSut();

        var result = await repo.GetAccountInfoAsync(new User(TestEmail));

        Assert.Single(result.Value.Domains);
        Assert.Contains(result.Value.Domains, d => d.Name == "weesky.be");
    }

    [Fact]
    public async Task GetAccountInfo_WithOwnedDomains_ReturnsAllOwnedDomains()
    {
        var (repo, context) = CreateSut();
        var userId = context.Users.First(u => u.Name == "john").Id;
        context.DomainsOwnerships.Add(new MailDomainOwnership { DomainId = "OTH", UserId = userId });
        context.SaveChanges();

        var result = await repo.GetAccountInfoAsync(new User(TestEmail));

        Assert.Contains(result.Value.Domains, d => d.Name == "other.com");
    }

    [Fact]
    public async Task GetAccountInfo_WithOwnedDomains_AlsoIncludesPrimaryDomain()
    {
        var (repo, context) = CreateSut();
        var userId = context.Users.First(u => u.Name == "john").Id;
        context.DomainsOwnerships.Add(new MailDomainOwnership { DomainId = "OTH", UserId = userId });
        context.SaveChanges();

        var result = await repo.GetAccountInfoAsync(new User(TestEmail));

        Assert.Contains(result.Value.Domains, d => d.Name == "weesky.be");
        Assert.Contains(result.Value.Domains, d => d.Name == "other.com");
    }

    [Fact]
    public async Task GetAccountInfo_WhenPrimaryDomainIsInOwnerships_NoDuplicates()
    {
        var (repo, context) = CreateSut();
        var userId = context.Users.First(u => u.Name == "john").Id;
        context.DomainsOwnerships.Add(new MailDomainOwnership { DomainId = "WKY", UserId = userId });
        context.SaveChanges();

        var result = await repo.GetAccountInfoAsync(new User(TestEmail));

        Assert.Single(result.Value.Domains);
        Assert.Contains(result.Value.Domains, d => d.Name == "weesky.be");
    }

    [Fact]
    public async Task GetAccountInfo_WhenDomainNotFound_ReturnsFailure()
    {
        var (repo, _) = CreateSut();

        var result = await repo.GetAccountInfoAsync(new User("john@nonexistent.com"));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GetAccountInfo_WhenUserNotFound_ReturnsFailure()
    {
        var (repo, _) = CreateSut();

        var result = await repo.GetAccountInfoAsync(new User("nobody@weesky.be"));

        Assert.True(result.IsFailure);
    }

    // --- ChangePassword ---

    [Fact]
    public async Task ChangePassword_WithWeakPassword_ReturnsFailure()
    {
        var (repo, _) = CreateSut();

        var result = await repo.ChangePasswordAsync(new User(TestEmail), "short", TestPassword);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ChangePassword_WithWrongOldPassword_ReturnsFailure()
    {
        var (repo, _) = CreateSut();

        var result = await repo.ChangePasswordAsync(new User(TestEmail), "NewPassword123!", "WrongOldPassword");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ChangePassword_WhenUserNotFound_ReturnsFailure()
    {
        var (repo, _) = CreateSut();

        var result = await repo.ChangePasswordAsync(new User("nobody@weesky.be"), "NewPassword123!", TestPassword);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ChangePassword_WithValidData_ReturnsSuccess()
    {
        var (repo, _) = CreateSut();

        var result = await repo.ChangePasswordAsync(new User(TestEmail), "NewPassword123!", TestPassword);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("")]
    public async Task ChangePassword_WithPasswordTooShort_ReturnsFailure(string newPassword)
    {
        var (repo, _) = CreateSut();

        var result = await repo.ChangePasswordAsync(new User(TestEmail), newPassword, TestPassword);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ChangePassword_WhenDomainNotFound_ReturnsFailure()
    {
        var (repo, _) = CreateSut();

        var result = await repo.ChangePasswordAsync(new User("john@nonexistent.com"), "NewPassword123!", TestPassword);

        Assert.True(result.IsFailure);
    }

    // --- ChangeFullName ---

    [Fact]
    public async Task ChangeFullName_WithValidUser_ReturnsSuccess()
    {
        var (repo, _) = CreateSut();

        var result = await repo.ChangeFullNameAsync(new User(TestEmail), "New Name");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ChangeFullName_WithValidUser_PersistsNewName()
    {
        var (repo, context) = CreateSut();

        await repo.ChangeFullNameAsync(new User(TestEmail), "New Name");

        Assert.Equal("New Name", context.Users.First(u => u.Name == "john").FullName);
    }

    [Fact]
    public async Task ChangeFullName_WhenDomainNotFound_ReturnsFailure()
    {
        var (repo, _) = CreateSut();

        var result = await repo.ChangeFullNameAsync(new User("john@nonexistent.com"), "New Name");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ChangeFullName_WhenUserNotFound_ReturnsFailure()
    {
        var (repo, _) = CreateSut();

        var result = await repo.ChangeFullNameAsync(new User("nobody@weesky.be"), "New Name");

        Assert.True(result.IsFailure);
    }
}
