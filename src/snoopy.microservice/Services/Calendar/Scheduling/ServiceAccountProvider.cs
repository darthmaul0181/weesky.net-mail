using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

/// <summary>
/// A <see cref="CachedSingleton{T}"/> over a scoped store, hence the scope per load. The cache
/// lives in this process: the admin screen's invalidation reaches no other instance.
/// </summary>
internal sealed class ServiceAccountProvider(
    IServiceScopeFactory scopes, IServiceAccountSecretProtector protector, ILogger<ServiceAccountProvider> logger)
    : IServiceAccountProvider
{
    private readonly CachedSingleton<ServiceSmtpAccount> cache = new(ct => LoadAsync(scopes, protector, logger, ct));

    public bool? IsConfigured => cache.IsKnown;

    public Task<ServiceSmtpAccount?> GetAsync(CancellationToken cancellationToken) => cache.GetAsync(cancellationToken);

    public async Task InvalidateAsync(CancellationToken cancellationToken)
    {
        if (await cache.InvalidateAsync(cancellationToken) is { } ex)
            logger.LogWarning(ex, "The calendar service account could not be reloaded; the next invitation mail retries");
    }

    private static async Task<ServiceSmtpAccount?> LoadAsync(
        IServiceScopeFactory scopes, IServiceAccountSecretProtector protector, ILogger<ServiceAccountProvider> logger,
        CancellationToken cancellationToken)
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
