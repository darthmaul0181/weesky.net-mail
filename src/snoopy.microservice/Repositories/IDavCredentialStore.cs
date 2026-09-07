using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// The synchronisation state as the screen needs it. It carries no secret in any shape, and that
/// is the point: a screen able to show one again would force the table to hold it in clear.
/// </summary>
public readonly record struct DavCredentialState(
    bool Configured, bool CardDavEnabled, bool CalDavEnabled, DateTime? LastUsedAt);

/// <summary>What the authentication handler compares, read in one indexed lookup.</summary>
public readonly record struct DavCredentialRecord(
    bool CardDavEnabled, bool CalDavEnabled, string SecretHash, byte[] Salt)
{
    /// <summary>
    /// The synthesised one prints the digest, and the handler logging this record while debugging
    /// is exactly how a secret — hashed is still a secret — reaches a log file.
    /// </summary>
    public override string ToString() =>
        $"DavCredentialRecord {{ CardDavEnabled = {CardDavEnabled}, CalDavEnabled = {CalDavEnabled} }}";
}

public interface IDavCredentialStore
{
    /// <summary>Never null: an absent row is "never enabled", which the screen shows as off.</summary>
    Task<DavCredentialState> GetStateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The row, or null when the account never enabled synchronisation.</summary>
    Task<DavCredentialRecord?> FindAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Turns one service on. Returns the freshly drawn secret when this call created the row — the
    /// one and only moment it exists in clear — and null when it merely switched an existing row
    /// back on, including when a concurrent first enable won the race. A row born here carries the
    /// other service's switch explicitly off.
    /// </summary>
    /// <param name="userId">the account</param>
    /// <param name="protocol">the service being switched on</param>
    /// <param name="alongside">
    /// Runs inside the enabling transaction, after the row is written; null for none. How the
    /// default calendar shares the switch's transaction without a controller opening one. A retry
    /// of the execution strategy replays the whole body, so it must be idempotent.
    /// </param>
    /// <param name="cancellationToken">the request's own</param>
    Task<string?> EnableAsync(Guid userId, DavProtocol protocol, Func<Task>? alongside,
        CancellationToken cancellationToken);

    /// <summary>Switches one service off without destroying anything. Silent on an account with no
    /// row.</summary>
    Task DisableAsync(Guid userId, DavProtocol protocol, CancellationToken cancellationToken);

    /// <summary>
    /// Draws a new secret and a new salt on the existing row, returning the secret. Null when
    /// there is no row: regenerating what was never enabled is not a create.
    /// </summary>
    Task<string?> RegenerateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Stamps the last use. Called from the authentication path, so it creates nothing — an absent
    /// row is a zero-row write, never an error. <paramref name="usedAt"/> is UTC.
    /// </summary>
    Task TouchAsync(Guid userId, DateTime usedAt, CancellationToken cancellationToken);

    /// <summary>Removes the row if present. What a security-stamp rotation does (décision 2).</summary>
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken);
}
