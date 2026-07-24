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

        await store.ReplaceAsync(user,
            [Row("michel@weesky.be", "Michel", isDefault: true)], CancellationToken.None);

        var rows = await CreateStore(db).GetAsync(user, CancellationToken.None);
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
        await CreateStore(db).ReplaceAsync(user, [Row("a@weesky.be")], CancellationToken.None);

        var rows = await CreateStore(db).GetAsync(user, CancellationToken.None);
        Assert.Equal("a@weesky.be", Assert.Single(rows).Address);
    }

    [Fact]
    public async Task Replace_RemovesRowsAbsentFromTheNewSet()
    {
        var db = nameof(Replace_RemovesRowsAbsentFromTheNewSet);
        var user = Guid.NewGuid();
        var store = CreateStore(db);
        await store.ReplaceAsync(user, [Row("a@weesky.be"), Row("b@weesky.be")], CancellationToken.None);

        await CreateStore(db).ReplaceAsync(user, [Row("b@weesky.be", "B two")], CancellationToken.None);

        var rows = await CreateStore(db).GetAsync(user, CancellationToken.None);
        var row = Assert.Single(rows);
        Assert.Equal("b@weesky.be", row.Address);
        Assert.Equal("B two", row.DisplayName);
    }

    [Fact]
    public async Task Replace_WithAnEmptySetClearsTheUser()
    {
        var db = nameof(Replace_WithAnEmptySetClearsTheUser);
        var user = Guid.NewGuid();
        await CreateStore(db).ReplaceAsync(user, [Row("a@weesky.be")], CancellationToken.None);

        await CreateStore(db).ReplaceAsync(user, [], CancellationToken.None);

        Assert.Empty(await CreateStore(db).GetAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task Replace_LeavesOtherUsersAlone()
    {
        var db = nameof(Replace_LeavesOtherUsersAlone);
        var bob = Guid.NewGuid();
        var alice = Guid.NewGuid();
        await CreateStore(db).ReplaceAsync(bob, [Row("bob-alias@weesky.be")], CancellationToken.None);

        await CreateStore(db).ReplaceAsync(alice, [Row("a@weesky.be")], CancellationToken.None);

        Assert.Single(await CreateStore(db).GetAsync(bob, CancellationToken.None));
    }
}
