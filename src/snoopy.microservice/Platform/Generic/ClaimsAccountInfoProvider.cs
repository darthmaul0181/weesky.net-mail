using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Platform.Generic;

/// <summary>
/// The account as the token describes it, and nothing more: there is no directory to read a numeric
/// id, a display name, owned domains or an admin flag from. <see cref="AccountInfo.Mailbox"/> is the
/// domain *id* on the weesky platform; with no domain table here it carries the domain name split
/// off the address, which is the only identifier this deployment has for it — and
/// <see cref="AccountInfo.Domains"/> carries that same value as a single synthetic row, since the
/// documented invariant is that Mailbox matches one of the Domains ids and the frontend derives the
/// user's email address from the row it finds there (<c>lib/accountIdentity.ts</c>).
/// </summary>
internal sealed class ClaimsAccountInfoProvider : IAccountInfoProvider
{
    public Task<Result<AccountInfo>> GetAccountInfoAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return Task.FromResult(Result.Success(new AccountInfo
        {
            UserId = 0,
            UserName = user.Name,
            FullName = null,
            Mailbox = user.Domain,
            Domains = [new Domain { Id = user.Domain, Name = user.Domain }],
            IsAdmin = false,
        }));
    }

    /// <summary>
    /// No directory holds this mailbox, so nothing here can deactivate or delete it: the token and
    /// its security stamp are the whole of what makes a session current. The mail server still has
    /// the last word — an account it no longer serves fails IMAP authentication.
    /// </summary>
    public Task<bool> IsUsableAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}
