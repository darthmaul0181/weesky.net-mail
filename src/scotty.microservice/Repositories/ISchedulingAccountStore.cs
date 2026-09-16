using weesky.Scotty.Microservice.Data.Preferences;

namespace weesky.Scotty.Microservice.Repositories;

/// <summary>The single row of the calendar service account. It validates nothing: the caller does.</summary>
public interface ISchedulingAccountStore
{
    Task<SchedulingServiceAccount?> FindAsync(CancellationToken cancellationToken);

    /// <summary>Creates or rewrites the row with a new password, forgetting the last test. Last writer wins: a lost
    /// race is retried once. False, writing nothing, when it loses twice.</summary>
    Task<bool> SaveAsync(string host, int port, string security, string login, byte[] passwordCipher,
        CancellationToken cancellationToken);

    /// <summary>Rewrites the row keeping its password, only while it is still the version the caller checked the
    /// host and port against. False, writing nothing, once another save or a delete has landed since.</summary>
    Task<bool> SaveKeepingPasswordAsync(string host, int port, string security, string login, DateTime checkedVersion,
        CancellationToken cancellationToken);

    /// <summary>Idempotent, and last writer wins like <see cref="SaveAsync"/>: a delete committing after a
    /// concurrent save removes the fresh row too. False only when it loses twice.</summary>
    Task<bool> DeleteAsync(CancellationToken cancellationToken);

    /// <summary>False, writing nothing, when the row is gone or was saved again after <paramref name="testedVersion"/>,
    /// including by a save that commits while this write runs.</summary>
    Task<bool> RecordTestAsync(DateTime testedAt, bool ok, DateTime testedVersion, CancellationToken cancellationToken);
}
