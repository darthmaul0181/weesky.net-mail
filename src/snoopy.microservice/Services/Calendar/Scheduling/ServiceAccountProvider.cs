using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

/// <summary>
/// A singleton over a scoped store, hence the scope per load. The cache lives in this process: the
/// admin screen's invalidation reaches no other instance.
/// </summary>
internal sealed class ServiceAccountProvider(
    IServiceScopeFactory scopes, IServiceAccountSecretProtector protector, ILogger<ServiceAccountProvider> logger)
    : IServiceAccountProvider
{
    private readonly SemaphoreSlim loading = new(1, 1);
    private readonly Lock swap = new();
    private LoadedServiceAccount? loaded;
    private long generation;

    public bool? IsConfigured => Volatile.Read(ref loaded) is { } known ? known.Account is not null : null;

    public async Task<ServiceSmtpAccount?> GetAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref loaded) is { } cached) return cached.Account;

        await loading.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref loaded) is { } loadedMeanwhile) return loadedMeanwhile.Account;

            long loadedFor;
            lock (swap) loadedFor = generation;
            var account = await LoadAsync(cancellationToken);
            // An invalidation that landed during the read: this result may predate the save, so it is not kept.
            lock (swap)
                if (loadedFor == generation) loaded = new LoadedServiceAccount(account);
            return account;
        }
        finally
        {
            loading.Release();
        }
    }

    public async Task InvalidateAsync(CancellationToken cancellationToken)
    {
        lock (swap)
        {
            generation++;
            loaded = null;
        }
        try
        {
            await GetAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The calendar service account could not be reloaded; the next invitation mail retries");
        }
    }

    private async Task<ServiceSmtpAccount?> LoadAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<ISchedulingAccountStore>().FindAsync(cancellationToken);
        if (row is null) return null;

        var account = ServiceSmtpAccount.Open(row, protector, out _);
        if (account is null)
            logger.LogWarning(
                "The calendar service account is stored but unusable — its password no longer decrypts, or the row was altered by hand; " +
                "enter it again in Administration. Devices' invitation mails are refused meanwhile");
        return account;
    }
}
