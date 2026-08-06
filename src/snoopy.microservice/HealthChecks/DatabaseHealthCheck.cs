using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.HealthChecks;

// One check probing both databases, not two named ones: /health is mapped with the default
// response writer, which only ever emits the aggregate status, so a second named check would
// buy an operator nothing visible. Which database is down still needs to be told apart, so the
// description below names it instead.
internal sealed class DatabaseHealthCheck(
    ApplicationDbContext accountsContext,
    PreferencesDbContext preferencesContext,
    ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var accountsReachable = await CanConnectAsync("accounts", accountsContext, cancellationToken);
        var preferencesReachable = await CanConnectAsync("preferences", preferencesContext, cancellationToken);

        if (accountsReachable && preferencesReachable) return HealthCheckResult.Healthy();

        var unreachable = new List<string>();
        if (!accountsReachable) unreachable.Add("accounts");
        if (!preferencesReachable) unreachable.Add("preferences");

        return HealthCheckResult.Unhealthy($"Unreachable database(s): {string.Join(", ", unreachable)}");
    }

    private async Task<bool> CanConnectAsync(string name, DbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Never surface ex.Message here: a connection failure names the host, port and user.
            logger.LogWarning(ex, "Health check failed to reach the {Database} database", name);
            return false;
        }
    }
}
