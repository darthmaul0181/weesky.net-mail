using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.HealthChecks;

// /health is mapped with the default response writer, which only ever emits the aggregate status,
// so which database is down still needs to be told apart: the description names it. A platform
// bringing a database of its own registers a check of its own next to this one.
internal sealed class DatabaseHealthCheck(
    PreferencesDbContext preferencesContext,
    ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return await CanConnectAsync(preferencesContext, cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Unreachable database(s): preferences");
    }

    private async Task<bool> CanConnectAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Never surface ex.Message here: a connection failure names the host, port and user.
            logger.LogWarning(ex, "Health check failed to reach the {Database} database", "preferences");
            return false;
        }
    }
}
