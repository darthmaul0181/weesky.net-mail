using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class SendingIdentityStoreTests
{
    private static SendingIdentityStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName));

    private static SendingIdentity Row(string address, string name = "Someone", bool isDefault = false) =>
        new() { Address = address, DisplayName = name, IsDefault = isDefault };

    [Fact]
    public async Task Replace_WritesTheRowsUnderTheUser()
    {
        var db = nameof(Replace_WritesTheRowsUnderTheUser);
        var user = Guid.NewGuid();
        var store = CreateStore(db);

        await store.ReplaceAsync(user, AccountScope.Primary,
            [Row("michel@weesky.be", "Michel", isDefault: true)], CancellationToken.None);

        var rows = await CreateStore(db).GetAsync(user, AccountScope.Primary, CancellationToken.None);
        var row = Assert.Single(rows);
        Assert.Equal(user, row.UserId);
        Assert.Equal("michel@weesky.be", row.Address);
        Assert.Equal("Michel", row.DisplayName);
        Assert.True(row.IsDefault);
    }

    [Fact]
    public async Task Replace_ThenGet_RoundTripsByUserId()
    {
        var db = nameof(Replace_ThenGet_RoundTripsByUserId);
        var user = Guid.NewGuid();
        await CreateStore(db).ReplaceAsync(user, AccountScope.Primary, [Row("a@weesky.be")], CancellationToken.None);

        var rows = await CreateStore(db).GetAsync(user, AccountScope.Primary, CancellationToken.None);
        Assert.Equal("a@weesky.be", Assert.Single(rows).Address);
    }

    [Fact]
    public async Task Replace_RemovesRowsAbsentFromTheNewSet()
    {
        var db = nameof(Replace_RemovesRowsAbsentFromTheNewSet);
        var user = Guid.NewGuid();
        var store = CreateStore(db);
        await store.ReplaceAsync(user, AccountScope.Primary, [Row("a@weesky.be"), Row("b@weesky.be")], CancellationToken.None);

        await CreateStore(db).ReplaceAsync(user, AccountScope.Primary, [Row("b@weesky.be", "B two")], CancellationToken.None);

        var rows = await CreateStore(db).GetAsync(user, AccountScope.Primary, CancellationToken.None);
        var row = Assert.Single(rows);
        Assert.Equal("b@weesky.be", row.Address);
        Assert.Equal("B two", row.DisplayName);
    }

    [Fact]
    public async Task Replace_WithAnEmptySetClearsTheUser()
    {
        var db = nameof(Replace_WithAnEmptySetClearsTheUser);
        var user = Guid.NewGuid();
        await CreateStore(db).ReplaceAsync(user, AccountScope.Primary, [Row("a@weesky.be")], CancellationToken.None);

        await CreateStore(db).ReplaceAsync(user, AccountScope.Primary, [], CancellationToken.None);

        Assert.Empty(await CreateStore(db).GetAsync(user, AccountScope.Primary, CancellationToken.None));
    }

    [Fact]
    public async Task Replace_LeavesOtherUsersAlone()
    {
        var db = nameof(Replace_LeavesOtherUsersAlone);
        var bob = Guid.NewGuid();
        var alice = Guid.NewGuid();
        await CreateStore(db).ReplaceAsync(bob, AccountScope.Primary, [Row("bob-alias@weesky.be")], CancellationToken.None);

        await CreateStore(db).ReplaceAsync(alice, AccountScope.Primary, [Row("a@weesky.be")], CancellationToken.None);

        Assert.Single(await CreateStore(db).GetAsync(bob, AccountScope.Primary, CancellationToken.None));
    }

    // A connected mailbox has its own From list: replacing one must not empty the other.
    [Fact]
    public async Task Replace_LeavesTheUsersOtherAccountsAlone()
    {
        var db = nameof(Replace_LeavesTheUsersOtherAccountsAlone);
        var user = Guid.NewGuid();
        var connected = Guid.NewGuid().ToString();
        await CreateStore(db).ReplaceAsync(user, connected, [Row("shared@weesky.be")], CancellationToken.None);

        await CreateStore(db).ReplaceAsync(user, AccountScope.Primary, [Row("a@weesky.be")], CancellationToken.None);

        var rows = await CreateStore(db).GetAsync(user, connected, CancellationToken.None);
        var row = Assert.Single(rows);
        Assert.Equal("shared@weesky.be", row.Address);
        Assert.Equal(connected, row.AccountId);
    }

    // The batch read behind the connected-accounts settings page: one call must return every
    // account's rows so the caller can group them in memory instead of reading account by account.
    [Fact]
    public async Task GetAll_ReturnsRowsFromEveryAccountOfTheUser()
    {
        var db = nameof(GetAll_ReturnsRowsFromEveryAccountOfTheUser);
        var user = Guid.NewGuid();
        var firstAccount = Guid.NewGuid().ToString();
        var secondAccount = Guid.NewGuid().ToString();
        var store = CreateStore(db);
        await store.ReplaceAsync(user, AccountScope.Primary, [Row("primary@weesky.be")], CancellationToken.None);
        await CreateStore(db).ReplaceAsync(user, firstAccount, [Row("a@weesky.be", "A")], CancellationToken.None);
        await CreateStore(db).ReplaceAsync(user, secondAccount, [Row("b@weesky.be", "B")], CancellationToken.None);

        var rows = await CreateStore(db).GetAllAsync(user, CancellationToken.None);

        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r.AccountId == AccountScope.Primary && r.Address == "primary@weesky.be");
        Assert.Contains(rows, r => r.AccountId == firstAccount && r.Address == "a@weesky.be");
        Assert.Contains(rows, r => r.AccountId == secondAccount && r.Address == "b@weesky.be");
    }

    [Fact]
    public async Task GetAll_LeavesOtherUsersAlone()
    {
        var db = nameof(GetAll_LeavesOtherUsersAlone);
        var bob = Guid.NewGuid();
        var alice = Guid.NewGuid();
        await CreateStore(db).ReplaceAsync(bob, AccountScope.Primary, [Row("bob@weesky.be")], CancellationToken.None);
        await CreateStore(db).ReplaceAsync(alice, AccountScope.Primary, [Row("a@weesky.be")], CancellationToken.None);

        var rows = await CreateStore(db).GetAllAsync(bob, CancellationToken.None);

        Assert.Equal("bob@weesky.be", Assert.Single(rows).Address);
    }
}
