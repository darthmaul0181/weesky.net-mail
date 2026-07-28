using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class AppSettingStoreTests
{
    private static AppSettingStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName));

    [Fact]
    public async Task Set_InsertsThenUpdatesTheSameRow()
    {
        var store = CreateStore(nameof(Set_InsertsThenUpdatesTheSameRow));

        await store.SetAsync(AppSettings.Name, "First", CancellationToken.None);
        await store.SetAsync(AppSettings.Name, "Second", CancellationToken.None);

        var row = Assert.Single(await store.GetAsync(CancellationToken.None));
        Assert.Equal("Second", row.SettingValue);
    }

    [Fact]
    public async Task Get_ReturnsEveryStoredRow()
    {
        var store = CreateStore(nameof(Get_ReturnsEveryStoredRow));
        await store.SetAsync(AppSettings.Name, "Snoopy mail", CancellationToken.None);
        await store.SetAsync(AppSettings.Installable, "true", CancellationToken.None);

        var rows = await store.GetAsync(CancellationToken.None);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task Set_StampsUpdatedAt()
    {
        var store = CreateStore(nameof(Set_StampsUpdatedAt));

        await store.SetAsync(AppSettings.ShortName, "Snoopy", CancellationToken.None);

        var row = Assert.Single(await store.GetAsync(CancellationToken.None));
        Assert.NotEqual(default, row.UpdatedAt);
    }
}
