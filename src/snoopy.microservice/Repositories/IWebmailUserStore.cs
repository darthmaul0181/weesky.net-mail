namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>The webmail account row as the session layer needs it: its key and its revocation stamp.</summary>
public readonly record struct WebmailAccount(Guid Id, Guid SecurityStamp);

public interface IWebmailUserStore
{
    /// <summary>
    /// Ensures the account's row exists and stamps the login. Called once per login, never per
    /// request. Returns the stable GUID (created if absent) and the current security stamp.
    /// Email is canonicalised.
    /// </summary>
    Task<WebmailAccount> RegisterLoginAsync(string email, CancellationToken cancellationToken);

    /// <summary>The account's key and stamp, or null when no row exists. Read on the session path.</summary>
    Task<WebmailAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Draws a new security stamp, which invalidates every token already issued for this account,
    /// and destroys its synchronisation secret in the same transaction — every caller of this is a
    /// gesture of taking control back, and a secret surviving one of them would leave the whole
    /// address book open. The DAV clients are to be reconfigured; the screens that trigger it say so.
    /// Returns the new stamp so the caller can re-issue its own session rather than sign itself out.
    /// </summary>
    Task<Guid> RotateSecurityStampAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// The user's KDF salt, generated and persisted on first need. Every connected-account cipher
    /// hangs off the key this salt derives, so the value is written once and never rotated.
    /// </summary>
    Task<byte[]> GetOrCreateKdfSaltAsync(string email, CancellationToken cancellationToken);

    /// <summary>Removes the account's row if present (0 rows = success). The FK cascade removes preferences.</summary>
    Task DeleteByEmailAsync(string email, CancellationToken cancellationToken);
}
