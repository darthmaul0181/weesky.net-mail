using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class ConnectedAccountStoreTests
{
    private static ConnectedAccountStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName));

    private static ConnectedAccount Account(
        Guid userId, string email = "shared@weesky.be", Guid? domainId = null) =>
        new() { UserId = userId, DomainId = domainId, Email = email, Cipher = [1, 2, 3] };

    [Fact]
    public async Task CreateAsync_CreatesTheRowAndItsDefaultIdentity()
    {
        var db = nameof(CreateAsync_CreatesTheRowAndItsDefaultIdentity);
        var user = Guid.NewGuid();

        var created = await CreateStore(db).CreateAsync(Account(user), CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.NotEqual(Guid.Empty, created.Value.Id);
        Assert.NotEqual(default, created.Value.CreationDate);

        var context = new PreferencesTestDbContext(db);
        var identity = Assert.Single(context.SendingIdentities);
        Assert.Equal(user, identity.UserId);
        Assert.Equal(created.Value.Id.ToString(), identity.AccountId);
        Assert.Equal(created.Value.Email, identity.Address);
        Assert.Equal(string.Empty, identity.DisplayName);
        Assert.True(identity.IsDefault);
    }

    // The table collates binary and the login goes out as stored: folding on the way in is what
    // keeps one mailbox from becoming two rows.
    [Fact]
    public async Task CreateAsync_FoldsTheEmail()
    {
        var db = nameof(CreateAsync_FoldsTheEmail);

        var created = await CreateStore(db)
            .CreateAsync(Account(Guid.NewGuid(), " Shared@Weesky.BE "), CancellationToken.None);

        Assert.Equal("shared@weesky.be", created.Value.Email);
    }

    // MariaDB never collides two NULLs, so uq_connected_accounts_target does not catch a
    // duplicate local mailbox — the store has to.
    [Fact]
    public async Task CreateAsync_RefusesADuplicateTarget()
    {
        var db = nameof(CreateAsync_RefusesADuplicateTarget);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(Account(user), CancellationToken.None);

        var again = await CreateStore(db)
            .CreateAsync(Account(user, "  Shared@weesky.be "), CancellationToken.None);

        Assert.True(again.IsFailure);
        Assert.Equal(ConnectedAccountStore.AlreadyConnected, again.Error);
    }

    [Fact]
    public async Task CreateAsync_AllowsTheSameMailboxForAnotherUser()
    {
        var db = nameof(CreateAsync_AllowsTheSameMailboxForAnotherUser);
        await CreateStore(db).CreateAsync(Account(Guid.NewGuid()), CancellationToken.None);

        var other = await CreateStore(db).CreateAsync(Account(Guid.NewGuid()), CancellationToken.None);

        Assert.True(other.IsSuccess);
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyTheUsersAccounts()
    {
        var db = nameof(ListAsync_ReturnsOnlyTheUsersAccounts);
        var alice = Guid.NewGuid();
        await CreateStore(db).CreateAsync(Account(alice, "a@weesky.be"), CancellationToken.None);
        await CreateStore(db).CreateAsync(Account(Guid.NewGuid(), "b@weesky.be"), CancellationToken.None);

        var rows = await CreateStore(db).ListAsync(alice, CancellationToken.None);

        Assert.Equal("a@weesky.be", Assert.Single(rows).Email);
    }

    [Fact]
    public async Task FindAsync_ScopesByUser()
    {
        var db = nameof(FindAsync_ScopesByUser);
        var created = await CreateStore(db).CreateAsync(Account(Guid.NewGuid()), CancellationToken.None);

        Assert.Null(await CreateStore(db).FindAsync(Guid.NewGuid(), created.Value.Id, CancellationToken.None));
        Assert.NotNull(await CreateStore(db)
            .FindAsync(created.Value.UserId, created.Value.Id, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateCipherAsync_RewritesTheStoredCipher()
    {
        var db = nameof(UpdateCipherAsync_RewritesTheStoredCipher);
        var created = await CreateStore(db).CreateAsync(Account(Guid.NewGuid()), CancellationToken.None);

        var store = CreateStore(db);
        var row = await store.FindAsync(created.Value.UserId, created.Value.Id, CancellationToken.None);
        await store.UpdateCipherAsync(row!, [9, 9], CancellationToken.None);

        var reread = await CreateStore(db)
            .FindAsync(created.Value.UserId, created.Value.Id, CancellationToken.None);
        Assert.Equal<byte[]>([9, 9], reread!.Cipher);
    }

    [Fact]
    public async Task ReplaceCiphersAsync_RewritesEveryCipher()
    {
        var db = nameof(ReplaceCiphersAsync_RewritesEveryCipher);
        var user = Guid.NewGuid();
        var first = await CreateStore(db).CreateAsync(Account(user, "a@weesky.be"), CancellationToken.None);
        var second = await CreateStore(db).CreateAsync(Account(user, "b@weesky.be"), CancellationToken.None);

        await CreateStore(db).ReplaceCiphersAsync(user,
            new Dictionary<Guid, byte[]> { [first.Value.Id] = [7], [second.Value.Id] = [8] },
            CancellationToken.None);

        var rows = await CreateStore(db).ListAsync(user, CancellationToken.None);
        Assert.Equal<byte[]>([7], rows.Single(a => a.Id == first.Value.Id).Cipher);
        Assert.Equal<byte[]>([8], rows.Single(a => a.Id == second.Value.Id).Cipher);
    }

    [Fact]
    public async Task ReplaceCiphersAsync_LeavesAnotherUsersAccountAlone()
    {
        var db = nameof(ReplaceCiphersAsync_LeavesAnotherUsersAccountAlone);
        var mine = await CreateStore(db).CreateAsync(Account(Guid.NewGuid()), CancellationToken.None);

        await CreateStore(db).ReplaceCiphersAsync(Guid.NewGuid(),
            new Dictionary<Guid, byte[]> { [mine.Value.Id] = [7] }, CancellationToken.None);

        var reread = await CreateStore(db).FindAsync(mine.Value.UserId, mine.Value.Id, CancellationToken.None);
        Assert.Equal<byte[]>([1, 2, 3], reread!.Cipher);
    }

    // No FK carries account_id — the sentinel forbids it — so the cascade is ours to run.
    [Fact]
    public async Task DeleteAsync_PurgesIdentitiesAndOverrides()
    {
        var db = nameof(DeleteAsync_PurgesIdentitiesAndOverrides);
        var user = Guid.NewGuid();
        var created = await CreateStore(db).CreateAsync(Account(user), CancellationToken.None);
        var accountId = created.Value.Id.ToString();

        var seed = new PreferencesTestDbContext(db);
        seed.FolderRoleOverrides.Add(new FolderRoleOverride
        {
            UserId = user, AccountId = accountId, Role = "trash", FolderPath = "Trash"
        });
        seed.FolderRoleOverrides.Add(new FolderRoleOverride
        {
            UserId = user, AccountId = string.Empty, Role = "trash", FolderPath = "Corbeille"
        });
        seed.SendingIdentities.Add(new SendingIdentity
        {
            UserId = user, AccountId = string.Empty, Address = "primary@weesky.be"
        });
        await seed.SaveChangesAsync(CancellationToken.None);

        await CreateStore(db).DeleteAsync(user, created.Value.Id, CancellationToken.None);

        var context = new PreferencesTestDbContext(db);
        Assert.Empty(context.ConnectedAccounts);
        Assert.Equal("Corbeille", Assert.Single(context.FolderRoleOverrides).FolderPath);
        Assert.Equal("primary@weesky.be", Assert.Single(context.SendingIdentities).Address);
    }

    [Fact]
    public async Task DeleteAsync_IgnoresAnotherUsersAccount()
    {
        var db = nameof(DeleteAsync_IgnoresAnotherUsersAccount);
        var created = await CreateStore(db).CreateAsync(Account(Guid.NewGuid()), CancellationToken.None);

        await CreateStore(db).DeleteAsync(Guid.NewGuid(), created.Value.Id, CancellationToken.None);

        Assert.NotNull(await CreateStore(db)
            .FindAsync(created.Value.UserId, created.Value.Id, CancellationToken.None));
    }
}
