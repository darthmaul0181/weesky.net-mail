using Microsoft.Extensions.Diagnostics.HealthChecks;
using weesky.Snoopy.Microservice.Data;

namespace weesky.Snoopy.Microservice.HealthChecks;

internal sealed class DatabaseHealthCheck(ApplicationDbContext dbContext, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    private const string UnhealthyDescription = "Database is unreachable.";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy(UnhealthyDescription);
        }
        catch (Exception ex)
        {
            // Never surface ex.Message here: a connection failure names the host, port and user.
            logger.LogError(ex, "Database health check failed");
            return HealthCheckResult.Unhealthy(UnhealthyDescription);
        }
    }
}
