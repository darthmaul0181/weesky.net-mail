using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Daily GC over the trusted senders, so an allowance nobody uses any more does not outlive its
/// usefulness. It is not what bounds the table — the per-account cap in
/// <see cref="TrustedSenderStore"/> is.
/// </summary>
internal sealed class TrustedSenderSweeper(
    IServiceScopeFactory scopes,
    IOptions<TrustedSenderOptions> options,
    ILogger<TrustedSenderSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // A sweep that throws must not take the host down with it; the next tick retries.
                logger.LogError(ex, "The trusted sender sweep failed");
            }
        }
    }

    /// <summary>
    /// One pass. Opens a scope of its own because the store and its DbContext are scoped while
    /// this service is a singleton — injecting the store directly compiles and throws here.
    /// </summary>
    internal async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITrustedSenderStore>();

        var removed = await store.SweepExpiredAsync(
            TimeSpan.FromDays(options.Value.RetentionDays), cancellationToken);

        // Every tick logs, zero included: the line is also the sweeper's heartbeat.
        logger.LogInformation("Trusted sender sweep: {Count} row(s) removed", removed);
    }
}
