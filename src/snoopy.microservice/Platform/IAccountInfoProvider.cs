using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Platform;

/// <summary>What the platform can say about the authenticated account itself.</summary>
public interface IAccountInfoProvider
{
    Task<Result<AccountInfo>> GetAccountInfoAsync(User user, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the platform still holds this address as a usable mailbox. It is what a live session
    /// is re-checked against on every request, so it must answer for a deleted *and* a deactivated
    /// account — a platform holding no directory has nothing to revoke and answers true.
    /// </summary>
    Task<bool> IsUsableAsync(string email, CancellationToken cancellationToken);
}
