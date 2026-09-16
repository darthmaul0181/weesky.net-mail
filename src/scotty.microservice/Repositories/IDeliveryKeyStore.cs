using weesky.Scotty.Microservice.Data.Preferences;

namespace weesky.Scotty.Microservice.Repositories;

public enum DeliveryKeyWrite { Written, NoKey, Conflict }

/// <summary>The single row of the delivery key. It validates nothing: the caller does.</summary>
public interface IDeliveryKeyStore
{
    Task<DeliveryReplyKey?> FindAsync(CancellationToken cancellationToken);

    /// <summary>Creates or rewrites the row with a new hash, forgetting the last call and keeping the
    /// switch. Last writer wins: a lost race is retried once; false, writing nothing, when it loses twice.</summary>
    Task<bool> ReplaceAsync(byte[] keyHash, DateTime now, CancellationToken cancellationToken);

    Task<DeliveryKeyWrite> SetEnabledAsync(bool enabled, DateTime now, CancellationToken cancellationToken);

    Task<DeliveryKeyWrite> DeleteAsync(CancellationToken cancellationToken);

    /// <summary>Moves last_call_at alone. Never loses against the screen and never makes it lose: the
    /// token is left untouched and a lost race is simply retried. Nothing without a row.</summary>
    Task RecordCallAsync(DateTime calledAt, CancellationToken cancellationToken);
}
