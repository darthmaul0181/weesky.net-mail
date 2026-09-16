using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Repositories;

public sealed class DeliveryKeyStoreTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateTime T0 = new(2026, 9, 14, 18, 0, 0, DateTimeKind.Utc);
    private readonly string database = Guid.NewGuid().ToString("N");
    private static readonly byte[] HashA = Enumerable.Repeat((byte)1, 32).ToArray();
    private static readonly byte[] HashB = Enumerable.Repeat((byte)2, 32).ToArray();

    private DeliveryKeyStore Store() => new(new PreferencesTestDbContext(database));

    [Fact]
    public async Task Replace_CreatesTheRow_ThenRewritesIt_ForgettingTheLastCall_KeepingEnabled()
    {
        Assert.Null(await Store().FindAsync(None));
        Assert.True(await Store().ReplaceAsync(HashA, T0, None));
        Assert.Equal(DeliveryKeyWrite.Written, await Store().SetEnabledAsync(true, T0.AddMinutes(1), None));
        await Store().RecordCallAsync(T0.AddMinutes(2), None);

        Assert.True(await Store().ReplaceAsync(HashB, T0.AddMinutes(3), None));

        var row = (await Store().FindAsync(None))!;
        // byte[] is reference-compared inside a ValueTuple even under Assert.Equal, so it is checked on its own.
        Assert.Equal(HashB, row.KeyHash);
        Assert.Equal((T0.AddMinutes(3), (DateTime?)null, true, T0.AddMinutes(3)),
            (row.CreatedAt, row.LastCallAt, row.Enabled, row.UpdatedAt));
    }

    [Fact]
    public async Task SetEnabled_And_Delete_SayNoKey_WhenNoRow()
    {
        Assert.Equal(DeliveryKeyWrite.NoKey, await Store().SetEnabledAsync(true, T0, None));
        Assert.Equal(DeliveryKeyWrite.NoKey, await Store().DeleteAsync(None));
    }

    [Fact]
    public async Task RecordCall_MovesLastCallAt_WithoutTouchingUpdatedAt()
    {
        await Store().ReplaceAsync(HashA, T0, None);

        await Store().RecordCallAsync(T0.AddMinutes(5), None);

        var row = (await Store().FindAsync(None))!;
        Assert.Equal((T0.AddMinutes(5), T0), (row.LastCallAt, row.UpdatedAt));
    }

    [Fact]
    public async Task RecordCall_WithoutARow_DoesNothing()
    {
        await Store().RecordCallAsync(T0, None);
        Assert.Null(await Store().FindAsync(None));
    }

    [Fact]
    public async Task Delete_RemovesTheRow()
    {
        await Store().ReplaceAsync(HashA, T0, None);
        Assert.Equal(DeliveryKeyWrite.Written, await Store().DeleteAsync(None));
        Assert.Null(await Store().FindAsync(None));
    }
}
