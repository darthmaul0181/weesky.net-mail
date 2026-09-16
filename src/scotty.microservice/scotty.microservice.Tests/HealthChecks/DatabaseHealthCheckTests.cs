using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using weesky.Scotty.Microservice.HealthChecks;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.HealthChecks;

public sealed class DatabaseHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenThePreferencesDatabaseIsReachable_ReturnsHealthy()
    {
        var preferences = new PreferencesTestDbContext(Guid.NewGuid().ToString());
        var healthCheck = new DatabaseHealthCheck(preferences, NullLogger<DatabaseHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenPreferencesDatabaseUnreachable_ReturnsUnhealthyNamingPreferences()
    {
        var preferences = new PreferencesTestDbContext(Guid.NewGuid().ToString());
        preferences.Dispose();
        var healthCheck = new DatabaseHealthCheck(preferences, NullLogger<DatabaseHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Unreachable database(s): preferences", result.Description);
        Assert.DoesNotContain(nameof(ObjectDisposedException), result.Description);
        Assert.DoesNotContain("Server=", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Uid=", result.Description, StringComparison.OrdinalIgnoreCase);
    }
}
