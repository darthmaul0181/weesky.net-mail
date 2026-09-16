namespace weesky.Scotty.Microservice.Services.Calendar.Delivery;

public sealed record DeliveryKeySnapshot(byte[] KeyHash, bool Enabled);

/// <summary>The delivery key as Administration stored it, read once and kept in memory until the
/// admin screen writes it again. The cache lives in this process (same limit as the service account).</summary>
public interface IDeliveryKeyProvider
{
    /// <summary>Null when no key is stored; throws when the database cannot be read.</summary>
    Task<DeliveryKeySnapshot?> GetAsync(CancellationToken cancellationToken);

    /// <summary>Forgets the cached key and loads the stored one again. Never throws.</summary>
    Task InvalidateAsync(CancellationToken cancellationToken);
}
