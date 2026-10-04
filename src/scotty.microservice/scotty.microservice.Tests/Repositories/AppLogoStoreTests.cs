using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Repositories;

public sealed class AppLogoStoreTests
{
    private static AppLogoStore CreateStore(string dbName) => new(new PreferencesTestDbContext(dbName));

    private static Dictionary<int, byte[]> Images(byte mark) =>
        new() { [32] = [mark, 32], [192] = [mark, 192], [512] = [mark, 2] };

    [Fact]
    public async Task Empty_HasNoImageAndNoDate()
    {
        var store = CreateStore(nameof(Empty_HasNoImageAndNoDate));

        Assert.Null(await store.GetImageAsync(192, CancellationToken.None));
        Assert.Null(await store.GetUpdatedAtAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Replace_StoresEverySize()
    {
        var store = CreateStore(nameof(Replace_StoresEverySize));

        await store.ReplaceAsync(Images(1), CancellationToken.None);

        Assert.Equal([1, 32], await store.GetImageAsync(32, CancellationToken.None));
        Assert.Equal([1, 192], await store.GetImageAsync(192, CancellationToken.None));
        Assert.Equal([1, 2], await store.GetImageAsync(512, CancellationToken.None));
    }

    [Fact]
    public async Task Replace_OverwritesThePreviousLogoAndMovesTheDate()
    {
        var store = CreateStore(nameof(Replace_OverwritesThePreviousLogoAndMovesTheDate));
        await store.ReplaceAsync(Images(1), CancellationToken.None);
        var first = await store.GetUpdatedAtAsync(CancellationToken.None);
        await Task.Delay(5);

        await store.ReplaceAsync(Images(2), CancellationToken.None);

        Assert.Equal([2, 192], await store.GetImageAsync(192, CancellationToken.None));
        Assert.True(await store.GetUpdatedAtAsync(CancellationToken.None) > first);
    }

    [Fact]
    public async Task Clear_RemovesEverySize()
    {
        var store = CreateStore(nameof(Clear_RemovesEverySize));
        await store.ReplaceAsync(Images(1), CancellationToken.None);

        await store.ClearAsync(CancellationToken.None);

        Assert.Null(await store.GetImageAsync(32, CancellationToken.None));
        Assert.Null(await store.GetUpdatedAtAsync(CancellationToken.None));
    }
}
