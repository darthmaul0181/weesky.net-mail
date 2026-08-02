using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.HealthChecks;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.HealthChecks;

public sealed class DatabaseHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenBothDatabasesReachable_ReturnsHealthy()
    {
        var accounts = new TestDbContext(Guid.NewGuid().ToString());
        var preferences = new PreferencesTestDbContext(Guid.NewGuid().ToString());
        var healthCheck = new DatabaseHealthCheck(accounts, preferences, NullLogger<DatabaseHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    // CanConnectAsync returns false for an unreachable server rather than throwing; this is the
    // real-world outage shape, unlike a disposed context below.
    [Fact]
    public async Task CheckHealthAsync_WhenAccountsCanConnectAsyncReturnsFalse_ReturnsUnhealthyNamingAccounts()
    {
        var accounts = new UnreachableAccountsDbContext();
        var preferences = new PreferencesTestDbContext(Guid.NewGuid().ToString());
        var healthCheck = new DatabaseHealthCheck(accounts, preferences, NullLogger<DatabaseHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Unreachable database(s): accounts", result.Description);
        Assert.DoesNotContain("Server=", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenPreferencesDatabaseUnreachable_ReturnsUnhealthyNamingPreferences()
    {
        var accounts = new TestDbContext(Guid.NewGuid().ToString());
        var preferences = new PreferencesTestDbContext(Guid.NewGuid().ToString());
        preferences.Dispose();
        var healthCheck = new DatabaseHealthCheck(accounts, preferences, NullLogger<DatabaseHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Unreachable database(s): preferences", result.Description);
        Assert.DoesNotContain(nameof(ObjectDisposedException), result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenBothDatabasesUnreachable_DescriptionNamesBothAndOmitsExceptionDetails()
    {
        var accounts = new TestDbContext(Guid.NewGuid().ToString());
        accounts.Dispose();
        var preferences = new PreferencesTestDbContext(Guid.NewGuid().ToString());
        preferences.Dispose();
        var healthCheck = new DatabaseHealthCheck(accounts, preferences, NullLogger<DatabaseHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Unreachable database(s): accounts, preferences", result.Description);
        Assert.DoesNotContain("Disposed", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server=", result.Description, StringComparison.OrdinalIgnoreCase);
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
