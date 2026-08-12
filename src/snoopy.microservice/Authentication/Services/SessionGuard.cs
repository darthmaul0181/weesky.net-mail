using Microsoft.Extensions.Caching.Memory;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Authentication.Services;

internal sealed class SessionGuard(
    IAccountInfoProvider accounts,
    IWebmailUserStore webmailUsers,
    IMemoryCache cache) : ISessionGuard
{
    /// <summary>
    /// How long an account's state is reused across requests. It is the ceiling on how long a
    /// revoked session keeps working when the rotation happened on another instance or process;
    /// on this one <see cref="Forget"/> makes it immediate.
    /// </summary>
    internal static readonly TimeSpan CacheWindow = TimeSpan.FromSeconds(60);

    /// <summary>Null stamp means the account is gone, or was never registered in the webmail database.</summary>
    private readonly record struct AccountState(bool Usable, Guid? SecurityStamp);

    public async Task<bool> IsCurrentAsync(string email, Guid presentedStamp, CancellationToken cancellationToken)
    {
        var state = await cache.GetOrCreateAsync(CacheKey(email), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheWindow;

            var usable = await accounts.IsUsableAsync(email, cancellationToken);
            var account = usable ? await webmailUsers.FindByEmailAsync(email, cancellationToken) : null;

            return new AccountState(usable, account?.SecurityStamp);
        });

        if (!state.Usable) return false;

        // No stored stamp means no row to compare against; refusing is the safe reading, and the
        // account gets one back on its next login.
        return state.SecurityStamp is { } current && current == presentedStamp;
    }

    public void Forget(string email) => cache.Remove(CacheKey(email));

    private static string CacheKey(string email) => $"session-state:{email.Trim().ToLowerInvariant()}";
}
