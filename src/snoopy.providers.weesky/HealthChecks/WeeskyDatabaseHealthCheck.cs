using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using weesky.Snoopy.Providers.Weesky.Data;

namespace weesky.Snoopy.Providers.Weesky.HealthChecks;

/// <summary>
/// The dovecot database, probed next to the core's check over the preferences one. /health is
/// mapped with the default response writer, which only ever emits the aggregate status, so the
/// description is what tells an operator which of the two is down.
/// </summary>
internal sealed class WeeskyDatabaseHealthCheck(
    ApplicationDbContext accountsContext,
    ILogger<WeeskyDatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await accountsContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Unreachable database(s): accounts");
        }
        catch (Exception ex)
        {
            // Never surface ex.Message here: a connection failure names the host, port and user.
            logger.LogWarning(ex, "Health check failed to reach the {Database} database", "accounts");
            return HealthCheckResult.Unhealthy("Unreachable database(s): accounts");
        }
    }
}
