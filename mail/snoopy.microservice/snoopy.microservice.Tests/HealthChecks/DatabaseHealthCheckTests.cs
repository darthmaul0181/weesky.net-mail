using Microsoft.Extensions.Diagnostics.HealthChecks;
using weesky.Snoopy.Microservice.HealthChecks;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.HealthChecks;

public class DatabaseHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenDatabaseReachable_ReturnsHealthy()
    {
        var context = new TestDbContext(Guid.NewGuid().ToString());
        var healthCheck = new DatabaseHealthCheck(context);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenDatabaseUnreachable_ReturnsUnhealthy()
    {
        var context = new TestDbContext(Guid.NewGuid().ToString());
        context.Dispose();
        var healthCheck = new DatabaseHealthCheck(context);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
