using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using weesky.Snoopy.Providers.Weesky.Data;
using weesky.Snoopy.Providers.Weesky.HealthChecks;
using weesky.Snoopy.Providers.Weesky.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Providers.Weesky.Tests.HealthChecks;

public sealed class WeeskyDatabaseHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenTheDovecotDatabaseIsReachable_ReturnsHealthy()
    {
        var accounts = new TestDbContext(Guid.NewGuid().ToString());
        var healthCheck = new WeeskyDatabaseHealthCheck(accounts, NullLogger<WeeskyDatabaseHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    // CanConnectAsync returns false for an unreachable server rather than throwing; this is the
    // real-world outage shape, unlike a disposed context below.
    [Fact]
    public async Task CheckHealthAsync_WhenCanConnectAsyncReturnsFalse_ReturnsUnhealthyNamingAccounts()
    {
        var healthCheck = new WeeskyDatabaseHealthCheck(
            new UnreachableAccountsDbContext(), NullLogger<WeeskyDatabaseHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Unreachable database(s): accounts", result.Description);
        Assert.DoesNotContain("Server=", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenTheContextThrows_ReturnsUnhealthyWithoutTheExceptionDetails()
    {
        var accounts = new TestDbContext(Guid.NewGuid().ToString());
        accounts.Dispose();
        var healthCheck = new WeeskyDatabaseHealthCheck(accounts, NullLogger<WeeskyDatabaseHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Unreachable database(s): accounts", result.Description);
        Assert.DoesNotContain("Disposed", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Uid=", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    // A fake DatabaseFacade is the only way to exercise the returns-false branch: the InMemory
    // provider's CanConnectAsync always answers true, and a real broken connection is not a unit test.
    private sealed class UnreachableDatabaseFacade(DbContext context) : DatabaseFacade(context)
    {
        public override Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class UnreachableAccountsDbContext()
        : ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options)
    {
        public override DatabaseFacade Database => new UnreachableDatabaseFacade(this);
    }
}
