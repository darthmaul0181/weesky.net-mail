using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services.Calendar.Delivery;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services.Calendar.Delivery;

public sealed class DeliveryKeyProviderTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private readonly string database = Guid.NewGuid().ToString("N");

    private DeliveryKeyProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddScoped<IDeliveryKeyStore>(_ => new DeliveryKeyStore(new PreferencesTestDbContext(database)));
        return new DeliveryKeyProvider(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DeliveryKeyProvider>.Instance);
    }

    [Fact]
    public async Task Get_IsNullWithoutARow_AndCachesTheRow_UntilInvalidated()
    {
        var provider = Provider();
        Assert.Null(await provider.GetAsync(None));

        var hash = DeliveryKeys.Hash("k");
        await new DeliveryKeyStore(new PreferencesTestDbContext(database)).ReplaceAsync(hash, DateTime.UtcNow, None);
        Assert.Null(await provider.GetAsync(None));

        await provider.InvalidateAsync(None);
        var snapshot = await provider.GetAsync(None);
        // byte[] is reference-compared inside a ValueTuple even under Assert.Equal, so it is checked on its own.
        Assert.Equal(hash, snapshot!.KeyHash);
        Assert.False(snapshot.Enabled);
    }
}
