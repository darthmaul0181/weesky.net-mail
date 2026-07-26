using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class TrustedSenderSweeperTests
{
    private readonly Mock<ITrustedSenderStore> _store = new();

    // The store and its DbContext are scoped; this service is a singleton. Resolving the store
    // through a scope is the whole point of the test — injecting it directly compiles and throws
    // on the first tick, which no compiler will tell you.
    private TrustedSenderSweeper CreateSweeper(int retentionDays = 365)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _store.Object);

        return new TrustedSenderSweeper(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TrustedSenderOptions { RetentionDays = retentionDays }),
            NullLogger<TrustedSenderSweeper>.Instance);
    }

    [Fact]
    public async Task SweepOnce_ResolvesTheStoreFromAScopeAndSweeps()
    {
        _store.Setup(s => s.SweepExpiredAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(3);

        await CreateSweeper().SweepOnceAsync(CancellationToken.None);

        _store.Verify(s => s.SweepExpiredAsync(TimeSpan.FromDays(365), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SweepOnce_UsesTheConfiguredRetention()
    {
        _store.Setup(s => s.SweepExpiredAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(0);

        await CreateSweeper(retentionDays: 30).SweepOnceAsync(CancellationToken.None);

        _store.Verify(s => s.SweepExpiredAsync(TimeSpan.FromDays(30), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
