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

    // --- VerifyCredentials ---

    [Fact]
    public async Task VerifyCredentials_WithCorrectPassword_ReturnsTheUser()
    {
        var (repo, _) = CreateSut();

        var check = await repo.VerifyCredentialsAsync(TestEmail, TestPassword);

        Assert.Equal(CredentialResult.Ok, check.Result);
        Assert.Equal(TestEmail, check.User!.Email);
    }

    [Fact]
    public async Task VerifyCredentials_WithWrongPassword_Fails()
    {
        var (repo, _) = CreateSut();

        var check = await repo.VerifyCredentialsAsync(TestEmail, "WrongPassword!");

        Assert.Equal(CredentialResult.WrongPassword, check.Result);
        Assert.Null(check.User);
    }

    [Theory]
    [InlineData("john@nonexistent.com")]
    [InlineData("nobody@weesky.be")]
    [InlineData("notanemail")]
    public async Task VerifyCredentials_WithoutAMatchingMailbox_Fails(string email)
    {
        var (repo, _) = CreateSut();

        var check = await repo.VerifyCredentialsAsync(email, TestPassword);

        Assert.Equal(CredentialResult.UnknownAccount, check.Result);
        Assert.Null(check.User);
    }

    [Fact]
    public async Task VerifyCredentials_OnADeactivatedMailbox_FailsEvenWithTheRightPassword()
    {
        var (repo, context) = CreateSut();
        context.Users.First().Active = ActiveState.N;
        await context.SaveChangesAsync();

        var check = await repo.VerifyCredentialsAsync(TestEmail, TestPassword);

        Assert.Equal(CredentialResult.Deactivated, check.Result);
        Assert.Null(check.User);
    }

    // The decoy is what makes an unknown address cost what a wrong password costs. If it ever
    // stopped being a real crypt hash of the same shape, the equalisation would quietly go with
    // it and nothing else in the suite would notice.
    [Fact]
    public void AbsentAccountHash_IsARealSha512CryptHashOfProductionCost()
    {
        var hash = UsersRepository.AbsentAccountHash;

        Assert.StartsWith("$6$", hash);
        Assert.Equal(Crypter.Sha512.Crypt(TestPassword, hash), Crypter.Sha512.Crypt(TestPassword, hash));

        // Same cost parameters as a freshly generated salt: the prefix carries the rounds.
        var reference = Crypter.Sha512.Crypt("x", Crypter.Sha512.GenerateSalt());
        Assert.Equal(RoundsPrefix(reference), RoundsPrefix(hash));

        static string RoundsPrefix(string crypt) =>
            crypt.StartsWith("$6$rounds=", StringComparison.Ordinal)
                ? crypt[..crypt.IndexOf('$', "$6$rounds=".Length)]
                : "$6$";
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
