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
    /// Draws a new security stamp, which invalidates every token already issued for this account.
    /// Returns it so the caller can re-issue its own session rather than sign itself out.
    /// </summary>
    Task<Guid> RotateSecurityStampAsync(string email, CancellationToken cancellationToken);

    /// <summary>Removes the account's row if present (0 rows = success). The FK cascade removes preferences.</summary>
    Task DeleteByEmailAsync(string email, CancellationToken cancellationToken);
}
