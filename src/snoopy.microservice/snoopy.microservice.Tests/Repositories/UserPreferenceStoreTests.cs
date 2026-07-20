using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class UserPreferenceStoreTests
{
    // A database per test: the in-memory provider shares a store by name, so a shared one
    // would let one test see another's rows.
    private static UserPreferenceStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName));

    [Fact]
    public async Task GetAsync_ReturnsNothingForAnAccountThatNeverSetOne()
    {
        Assert.Empty(await CreateStore(Guid.NewGuid().ToString()).GetAsync("alice@weesky.be", CancellationToken.None));
    }

    [Fact]
    public async Task SetAsync_StoresThenReadsBack()
    {
        var db = Guid.NewGuid().ToString();
        var sut = CreateStore(db);

        await sut.SetAsync("alice@weesky.be", UserPreferences.MailPageSize, "50", CancellationToken.None);

        var stored = await sut.GetAsync("alice@weesky.be", CancellationToken.None);
        Assert.Equal("50", Assert.Single(stored).PreferenceValue);
    }

    // Setting the same key twice is a correction, not a second row: the key is half the
    // primary key, and a duplicate would make "the" value ambiguous.
    [Fact]
    public async Task SetAsync_OverwritesRatherThanAccumulating()
    {
        var db = Guid.NewGuid().ToString();
        var sut = CreateStore(db);

        await sut.SetAsync("alice@weesky.be", UserPreferences.MailPageSize, "50", CancellationToken.None);
        await sut.SetAsync("alice@weesky.be", UserPreferences.MailPageSize, "20", CancellationToken.None);

        var stored = await sut.GetAsync("alice@weesky.be", CancellationToken.None);
        Assert.Equal("20", Assert.Single(stored).PreferenceValue);
    }

    [Fact]
    public async Task SetAsync_KeepsAccountsApart()
    {
        var db = Guid.NewGuid().ToString();
        var sut = CreateStore(db);

        await sut.SetAsync("alice@weesky.be", UserPreferences.MailPageSize, "50", CancellationToken.None);
        await sut.SetAsync("bob@weesky.be", UserPreferences.MailPageSize, "10", CancellationToken.None);

        var alice = await sut.GetAsync("alice@weesky.be", CancellationToken.None);
        Assert.Equal("50", Assert.Single(alice).PreferenceValue);
    }

    [Fact]
    public async Task SetAsync_StampsTheTime()
    {
        var db = Guid.NewGuid().ToString();

        await CreateStore(db).SetAsync("alice@weesky.be", UserPreferences.MailShowPreview, "false", CancellationToken.None);

        var stored = await CreateStore(db).GetAsync("alice@weesky.be", CancellationToken.None);
        Assert.NotEqual(default, Assert.Single(stored).UpdatedAt);
    }
}
