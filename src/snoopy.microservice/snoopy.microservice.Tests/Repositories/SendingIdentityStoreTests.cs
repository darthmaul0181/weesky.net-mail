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
    public async Task Replace_WritesTheRowsUnderTheAccount()
    {
        var store = CreateStore(nameof(Replace_WritesTheRowsUnderTheAccount));

        await store.ReplaceAsync("alice@weesky.be",
            [Row("michel@weesky.be", "Michel", isDefault: true)], CancellationToken.None);

        var rows = await store.GetAsync("alice@weesky.be", CancellationToken.None);
        var row = Assert.Single(rows);
        Assert.Equal("alice@weesky.be", row.AccountId);
        Assert.Equal("michel@weesky.be", row.Address);
        Assert.Equal("Michel", row.DisplayName);
        Assert.True(row.IsDefault);
    }

    [Fact]
    public async Task Replace_RemovesRowsAbsentFromTheNewSet()
    {
        var store = CreateStore(nameof(Replace_RemovesRowsAbsentFromTheNewSet));
        await store.ReplaceAsync("alice@weesky.be",
            [Row("a@weesky.be"), Row("b@weesky.be")], CancellationToken.None);

        await store.ReplaceAsync("alice@weesky.be", [Row("b@weesky.be", "B two")], CancellationToken.None);

        var rows = await store.GetAsync("alice@weesky.be", CancellationToken.None);
        var row = Assert.Single(rows);
        Assert.Equal("b@weesky.be", row.Address);
        Assert.Equal("B two", row.DisplayName);
    }

    [Fact]
    public async Task Replace_WithAnEmptySetClearsTheAccount()
    {
        var store = CreateStore(nameof(Replace_WithAnEmptySetClearsTheAccount));
        await store.ReplaceAsync("alice@weesky.be", [Row("a@weesky.be")], CancellationToken.None);

        await store.ReplaceAsync("alice@weesky.be", [], CancellationToken.None);

        Assert.Empty(await store.GetAsync("alice@weesky.be", CancellationToken.None));
    }

    [Fact]
    public async Task Replace_LeavesOtherAccountsAlone()
    {
        var store = CreateStore(nameof(Replace_LeavesOtherAccountsAlone));
        await store.ReplaceAsync("bob@weesky.be", [Row("bob-alias@weesky.be")], CancellationToken.None);

        await store.ReplaceAsync("alice@weesky.be", [Row("a@weesky.be")], CancellationToken.None);

        Assert.Single(await store.GetAsync("bob@weesky.be", CancellationToken.None));
    }
}
