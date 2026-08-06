using CryptSharp.Core;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class AliasesRepositoryTests
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
    public async Task UserOwnsDomain_WithPrimaryDomain_ReturnsTrue()
    {
        var (repo, _, _) = CreateSut();

        Assert.True(await repo.UserOwnsDomainAsync(AuthUser, PrimaryDomain, CancellationToken.None));
    }

    [Fact]
    public async Task UserOwnsDomain_WithOwnedDomain_ReturnsTrue()
    {
        var (repo, _, _) = CreateSut();

        Assert.True(await repo.UserOwnsDomainAsync(AuthUser, OwnedDomain, CancellationToken.None));
    }

    [Fact]
    public async Task UserOwnsDomain_WithUnownedDomain_ReturnsFalse()
    {
        var (repo, _, _) = CreateSut();

        Assert.False(await repo.UserOwnsDomainAsync(AuthUser, UnownedDomain, CancellationToken.None));
    }

    [Fact]
    public async Task UserOwnsDomain_WhenUserNotFound_ReturnsFalse()
    {
        var (repo, _, _) = CreateSut();

        Assert.False(await repo.UserOwnsDomainAsync(new User("nobody@" + PrimaryDomain), OwnedDomain, CancellationToken.None));
    }

    [Fact]
    public async Task UserOwnsDomain_WhenDomainDoesNotExist_ReturnsFalse()
    {
        var (repo, _, _) = CreateSut();

        Assert.False(await repo.UserOwnsDomainAsync(AuthUser, "doesnotexist.com", CancellationToken.None));
    }

    // --- AddAlias ---

    [Fact]
    public async Task AddAlias_WithPrimaryDomain_ReturnsSuccess()
    {
        var (repo, _, _) = CreateSut();

        var result = await repo.AddAliasAsync(AuthUser, new Alias { Name = "johnny", Domain = PrimaryDomain }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AddAlias_WithOwnedDomain_ReturnsSuccess()
    {
        var (repo, _, _) = CreateSut();

        var result = await repo.AddAliasAsync(AuthUser, new Alias { Name = "johnny", Domain = OwnedDomain }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AddAlias_WithUnownedDomain_ReturnsFailure()
    {
        var (repo, _, _) = CreateSut();

        var result = await repo.AddAliasAsync(AuthUser, new Alias { Name = "johnny", Domain = UnownedDomain }, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task AddAlias_WhenAliasAlreadyExists_ReturnsFailure()
    {
        var (repo, _, _) = CreateSut();
        await repo.AddAliasAsync(AuthUser, new Alias { Name = "duplicate", Domain = PrimaryDomain }, CancellationToken.None);

        var result = await repo.AddAliasAsync(AuthUser, new Alias { Name = "duplicate", Domain = PrimaryDomain }, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task AddAlias_DuplicateCheckIsCaseInsensitive()
    {
        var (repo, _, _) = CreateSut();
        await repo.AddAliasAsync(AuthUser, new Alias { Name = "Duplicate", Domain = PrimaryDomain }, CancellationToken.None);

        var result = await repo.AddAliasAsync(AuthUser, new Alias { Name = "duplicate", Domain = PrimaryDomain }, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    // The unique key is (source_addr, source_domain): destination_user is not in it, so an address
    // another user already holds on a shared domain has to be refused here. Narrowed to this user,
    // the check passed and SaveChanges turned the constraint into a 500.
    [Fact]
    public async Task AddAlias_WhenAnotherUserHoldsTheAddress_ReturnsFailure()
    {
        var (repo, context, _) = CreateSut();
        var stranger = new MailUser
        {
            Name = "jane",
            Password = Crypter.MD5.Crypt("password"),
            DomainId = "OTH",
            Active = ActiveState.Y,
            FullName = "Jane Roe"
        };
        context.Users.Add(stranger);
        context.SaveChanges();
        context.Aliases.Add(new MailAlias { Name = "shared", Domain = "OTH", DestinationUserId = stranger.Id });
        context.SaveChanges();

        var result = await repo.AddAliasAsync(
            AuthUser, new Alias { Name = "shared", Domain = OwnedDomain }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("already exists", result.Error);
    }

    [Fact]
    public async Task AddAlias_WhenAnotherUserHoldsTheAddress_WritesNothing()
    {
        var (repo, context, _) = CreateSut();
        var stranger = new MailUser
        {
            Name = "jane",
            Password = Crypter.MD5.Crypt("password"),
            DomainId = "OTH",
            Active = ActiveState.Y,
            FullName = "Jane Roe"
        };
        context.Users.Add(stranger);
        context.SaveChanges();
        context.Aliases.Add(new MailAlias { Name = "shared", Domain = "OTH", DestinationUserId = stranger.Id });
        context.SaveChanges();

        await repo.AddAliasAsync(AuthUser, new Alias { Name = "shared", Domain = OwnedDomain }, CancellationToken.None);

        Assert.Single(context.Aliases.Where(a => a.Name == "shared" && a.Domain == "OTH"));
    }

    [Fact]
    public async Task AddAlias_WithNullAlias_ThrowsArgumentNullException()
    {
        var (repo, _, _) = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.AddAliasAsync(AuthUser, null!, CancellationToken.None));
    }

    [Fact]
    public async Task AddAlias_PersistsAliasToDatabase()
    {
        var (repo, context, userId) = CreateSut();

        await repo.AddAliasAsync(AuthUser, new Alias { Name = "newone", Domain = PrimaryDomain }, CancellationToken.None);

        Assert.True(context.Aliases.Any(a => a.Name == "newone" && a.DestinationUserId == userId));
    }

    // --- DeleteAlias ---

    [Fact]
    public async Task DeleteAlias_WhenAliasExists_ReturnsSuccess()
    {
        var (repo, _, _) = CreateSut();
        await repo.AddAliasAsync(AuthUser, new Alias { Name = "todelete", Domain = PrimaryDomain }, CancellationToken.None);

        var result = await repo.DeleteAliasAsync(AuthUser, new Alias { Name = "todelete", Domain = PrimaryDomain }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteAlias_WhenAliasNotFound_ReturnsFailure()
    {
        var (repo, _, _) = CreateSut();

        var result = await repo.DeleteAliasAsync(AuthUser, new Alias { Name = "nonexistent", Domain = PrimaryDomain }, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task DeleteAlias_WithUnownedDomain_ReturnsFailure()
    {
        var (repo, _, _) = CreateSut();

        var result = await repo.DeleteAliasAsync(AuthUser, new Alias { Name = "alias", Domain = UnownedDomain }, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task DeleteAlias_WithNullAlias_ThrowsArgumentNullException()
    {
        var (repo, _, _) = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.DeleteAliasAsync(AuthUser, null!, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAlias_RemovesAliasFromDatabase()
    {
        var (repo, context, userId) = CreateSut();
        await repo.AddAliasAsync(AuthUser, new Alias { Name = "gone", Domain = PrimaryDomain }, CancellationToken.None);

        await repo.DeleteAliasAsync(AuthUser, new Alias { Name = "gone", Domain = PrimaryDomain }, CancellationToken.None);

        Assert.False(context.Aliases.Any(a => a.Name == "gone" && a.DestinationUserId == userId));
    }

    // --- GetAliases ---

    [Fact]
    public async Task GetAliases_WithNoAliases_ReturnsEmptyList()
    {
        var (repo, _, _) = CreateSut();

        var aliases = (await repo.GetAliasesAsync(AuthUser, CancellationToken.None)).ToList();

        Assert.Empty(aliases);
    }

    [Fact]
    public async Task GetAliases_ReturnsPrimaryDomainAliases()
    {
        var (repo, _, _) = CreateSut();
        await repo.AddAliasAsync(AuthUser, new Alias { Name = "alias1", Domain = PrimaryDomain }, CancellationToken.None);

        var aliases = (await repo.GetAliasesAsync(AuthUser, CancellationToken.None)).ToList();

        Assert.Contains(aliases, a => a.Name == "alias1" && a.Domain == PrimaryDomain);
    }

    [Fact]
    public async Task GetAliases_ReturnsOwnedDomainAliases()
    {
        var (repo, _, _) = CreateSut();
        await repo.AddAliasAsync(AuthUser, new Alias { Name = "alias2", Domain = OwnedDomain }, CancellationToken.None);

        var aliases = (await repo.GetAliasesAsync(AuthUser, CancellationToken.None)).ToList();

        Assert.Contains(aliases, a => a.Name == "alias2" && a.Domain == OwnedDomain);
    }

    [Fact]
    public async Task GetAliases_DoesNotReturnAliasesOfOtherUsers()
    {
        var (repo, context, _) = CreateSut();
        var otherUser = new MailUser { Name = "alice", Password = "x", DomainId = "WKY", Active = ActiveState.Y, FullName = "Alice Smith" };
        context.Users.Add(otherUser);
        context.SaveChanges();
        var otherUserId = context.Users.First(u => u.Name == "alice").Id;
        context.Aliases.Add(new MailAlias { Name = "alice-alias", Domain = "WKY", DestinationUserId = otherUserId });
        context.SaveChanges();

        var aliases = (await repo.GetAliasesAsync(AuthUser, CancellationToken.None)).ToList();

        Assert.DoesNotContain(aliases, a => a.Name == "alice-alias");
    }

    // --- Cancellation ---

    [Fact]
    public async Task GetAliases_WithACancelledToken_DoesNotQuery()
    {
        var (repo, _, _) = CreateSut();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repo.GetAliasesAsync(AuthUser, cts.Token));
    }

    [Fact]
    public async Task AddAlias_WithACancelledToken_DoesNotWrite()
    {
        var (repo, context, _) = CreateSut();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repo.AddAliasAsync(AuthUser, new Alias { Name = "johnny", Domain = OwnedDomain }, cts.Token));
        Assert.Empty(context.Aliases);
    }
}
