using Microsoft.Extensions.Caching.Memory;
using Moq;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Repositories;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Repositories;

public sealed class CachedUserPreferenceStoreTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private readonly Mock<IUserPreferenceStore> inner = new();
    private readonly UserPreferenceCache cache = new(new MemoryCache(new MemoryCacheOptions()));

    private CachedUserPreferenceStore Store() => new(inner.Object, cache);

    private static List<UserPreference> Rows(Guid userId, string value) =>
        [new UserPreference { UserId = userId, PreferenceKey = "mail.showAvatars", PreferenceValue = value }];

    [Fact]
    public async Task RepeatedReads_HitTheDatabaseOnce()
    {
        inner.Setup(s => s.GetAsync(Alice, It.IsAny<CancellationToken>())).ReturnsAsync(Rows(Alice, "true"));
        var store = Store();

        await store.GetAsync(Alice, CancellationToken.None);
        var second = await store.GetAsync(Alice, CancellationToken.None);

        Assert.Equal("true", Assert.Single(second).PreferenceValue);
        inner.Verify(s => s.GetAsync(Alice, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AWrite_IsSeenByTheNextRead()
    {
        inner.SetupSequence(s => s.GetAsync(Alice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows(Alice, "false"))
            .ReturnsAsync(Rows(Alice, "true"));
        var store = Store();
        await store.GetAsync(Alice, CancellationToken.None);

        await store.SetAsync(Alice, "mail.showAvatars", "true", CancellationToken.None);

        Assert.Equal("true", Assert.Single(await store.GetAsync(Alice, CancellationToken.None)).PreferenceValue);
    }

    [Fact]
    public async Task EachAccountKeepsItsOwnRows()
    {
        inner.Setup(s => s.GetAsync(Alice, It.IsAny<CancellationToken>())).ReturnsAsync(Rows(Alice, "true"));
        inner.Setup(s => s.GetAsync(Bob, It.IsAny<CancellationToken>())).ReturnsAsync(Rows(Bob, "false"));
        var store = Store();
        await store.GetAsync(Alice, CancellationToken.None);
        await store.GetAsync(Bob, CancellationToken.None);

        await store.SetAsync(Bob, "mail.showAvatars", "true", CancellationToken.None);
        await store.GetAsync(Alice, CancellationToken.None);

        Assert.Equal("false", Assert.Single(await store.GetAsync(Bob, CancellationToken.None)).PreferenceValue);
        inner.Verify(s => s.GetAsync(Alice, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AReadOverlappingAWrite_IsNotKept()
    {
        // The read saw the row before the write landed: kept, it would answer the old value for a minute.
        var release = new TaskCompletionSource<IReadOnlyList<UserPreference>>();
        inner.SetupSequence(s => s.GetAsync(Alice, It.IsAny<CancellationToken>()))
            .Returns(release.Task)
            .ReturnsAsync(Rows(Alice, "true"));
        var store = Store();

        var stale = store.GetAsync(Alice, CancellationToken.None);
        await store.SetAsync(Alice, "mail.showAvatars", "true", CancellationToken.None);
        release.SetResult(Rows(Alice, "false"));
        await stale;

        Assert.Equal("true", Assert.Single(await store.GetAsync(Alice, CancellationToken.None)).PreferenceValue);
    }
}
