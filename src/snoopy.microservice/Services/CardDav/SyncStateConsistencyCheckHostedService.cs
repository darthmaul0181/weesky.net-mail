using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// Runs <see cref="SyncStateConsistencyCheck"/> once at startup. There is no <c>IStartupValidator</c>
/// in this repository — <c>AddHostedService</c> is the only startup mechanism
/// <c>ApplicationServicesConfiguration</c> uses, already twice, for the two sweepers — so this
/// follows that shape rather than introduce a third.
///
/// A thin wrapper rather than injecting <see cref="PreferencesDbContext"/> straight into the
/// service's own constructor: the context is scoped and this hosted service is a singleton,
/// exactly the trap <see cref="TrustedSenderSweeper"/>'s own doc comment documents — that compiles
/// and throws on the first run. Opens its own scope instead.
/// </summary>
internal sealed class SyncStateConsistencyCheckHostedService(
    IServiceScopeFactory scopes, ILogger<SyncStateConsistencyCheckHostedService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PreferencesDbContext>();
        var checkLogger = scope.ServiceProvider.GetRequiredService<ILogger<SyncStateConsistencyCheck>>();

        try
        {
            await new SyncStateConsistencyCheck(context, checkLogger).RunAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // A diagnostic that fails to run must not fail the whole host's startup with it —
            // PeriodicSweeper makes the same choice for its own periodic failures.
            logger.LogError(ex, "The sync state consistency check failed to run at startup");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
