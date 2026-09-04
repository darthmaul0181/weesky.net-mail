using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Authenticated IMAP connections kept between requests. Only <see cref="ScopedImapSessionProvider"/>
/// borrows; the login and connected-account probes never do. Saturation degrades to a single-use
/// connection — never a wait, never an error.
/// </summary>
public interface IImapConnectionPool
{
    /// <summary>A session over a pooled socket when one is fit, over a fresh one otherwise.
    /// Disposing the session returns the socket. <paramref name="userUid"/> is the borrower —
    /// what <see cref="Close"/> and <see cref="Revoke"/> index by.</summary>
    Task<Result<IImapSession>> BorrowAsync(MailAccountConnection connection, Guid userUid, CancellationToken cancellationToken);

    /// <summary>DELETE /Login: closes the user's idle sockets. Housekeeping, not revocation.</summary>
    void Close(Guid userUid);

    /// <summary>DELETE /Login/All: <see cref="Close"/>, and sockets the user has out right now
    /// are closed on return instead of pooled.</summary>
    void Revoke(Guid userUid);

    /// <summary>One pass over idle sockets: closes what is past its idle or absolute lifetime.
    /// Returns how many.</summary>
    Task<int> SweepAsync(CancellationToken cancellationToken);

    PoolStatistics Snapshot();
}

/// <summary>Counters, not events: the aggregate line the sweeper logs. <c>Keys</c> is how many
/// distinct identities still hold a place — the dictionary growth a credential rotation would cause.</summary>
public readonly record struct PoolStatistics(
    int Idle, int Borrowed, int Keys,
    long Borrows, long Reused, long Opened, long SingleUse, long HealthFailures,
    long ClosedIdle, long ClosedLifetime, long ClosedAtReturn, long Evicted);
