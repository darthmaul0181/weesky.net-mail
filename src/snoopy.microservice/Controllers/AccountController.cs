using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Authentication;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class AccountController(
    IUsersRepository usersRepository,
    IDovecotQuotaClient dovecotQuotaClient,
    IMailCredentialStore credentials,
    IWebmailUserStore webmailUsers,
    IConnectedAccountStore connectedAccounts,
    ISessionGuard sessions,
    ITokenManager tokens,
    IOptions<TokenConstants> tokenConstants,
    ILogger<AccountController> logger) : ApiBaseController
{

    /// <summary>
    /// Returns information about the authenticated user account
    /// </summary>
    /// <response code="200">Account information</response>
    /// <response code="401">Unauthenticated user</response>
    /// <response code="404">User not found</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountInfo>> GetAccountInfo(CancellationToken cancellationToken)
    {
        Result<AccountInfo> result = await usersRepository.GetAccountInfoAsync(AuthenticatedUser, cancellationToken);
        return FromResult(result, errorStatusCode: StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Returns the mailbox quota usage reported by Dovecot
    /// </summary>
    /// <response code="200">Quota information</response>
    /// <response code="401">Unauthenticated user</response>
    /// <response code="502">Unable to reach Dovecot</response>
    [HttpGet("Quota")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<Quota>> GetQuota(CancellationToken cancellationToken)
    {
        Result<Quota> result = await dovecotQuotaClient.GetQuotaAsync(AuthenticatedUser, cancellationToken);
        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>
    /// Returns the list of IMAP folders (mailboxes) for the authenticated user
    /// </summary>
    /// <response code="200">List of folder names</response>
    /// <response code="401">Unauthenticated user</response>
    /// <response code="502">Unable to reach Dovecot</response>
    [HttpGet("Folders")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<string>>> GetFolders(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<string>> result = await dovecotQuotaClient.GetMailboxesAsync(AuthenticatedUser, cancellationToken);
        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>
    /// Change the mailbox password
    /// </summary>
    /// <remarks>
    /// The credentials cookie carries the password every mail endpoint opens IMAP with, so it
    /// is re-issued here. Left alone it would keep the superseded password — and the sliding
    /// session would keep renewing it — leaving a live session whose every mail action fails
    /// authentication for the rest of the token's lifetime.
    ///
    /// The connected-account passwords are encrypted under a key the main password derives, so
    /// they are re-keyed in the same breath, before the new cookie is written.
    /// </remarks>
    /// <param name="secretChange">the new secret</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Secret changed successfully</response>
    /// <response code="400">Wrong credentials</response>
    /// <response code="401">Unauthenticated user</response>
    [HttpPatch("ChangeSecret")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> ChangePassword(SecretChange secretChange, CancellationToken cancellationToken)
    {
        Result result = await usersRepository.ChangePasswordAsync(
            AuthenticatedUser, secretChange.NewPassword, secretChange.OldPassword, cancellationToken);

        if (result.IsSuccess)
        {
            // Everything below is compensating work for a password that is already committed, so
            // none of it takes the request's token: a client that disconnects here would otherwise
            // be left with a changed password, un-rekeyed accounts and cookies holding the old one.

            // Before the cookie writes: the old key still has to be read off the incoming one.
            var newKek = await ReKeyConnectedAccountsAsync(secretChange, CancellationToken.None);

            // Rotating cuts every session of this account, which is the point — a password is
            // changed precisely when the other ones are no longer wanted. It also cuts this one,
            // so the caller is handed a fresh pair of cookies in the same response; without that
            // the user would sign themselves out by changing their password.
            var stamp = await webmailUsers.RotateSecurityStampAsync(AuthenticatedUser.Email, CancellationToken.None);
            sessions.Forget(AuthenticatedUser.Email);

            var renewed = new User(AuthenticatedUser.Email)
            {
                WebmailUid = AuthenticatedUser.WebmailUid,
                SecurityStamp = stamp
            };
            var token = tokens.Generate(renewed);
            if (!string.IsNullOrEmpty(token.Token))
                Response.WriteAuthCookie(tokenConstants.Value, token.Token);

            credentials.Store(Response, new MailCredentialPayload(secretChange.NewPassword, newKek),
                TimeSpan.FromMinutes(tokenConstants.Value.ExpiryInMinutes));
        }

        return FromResult(result, successStatusCode: StatusCodes.Status204NoContent);
    }

    /// <summary>
    /// Re-encrypts every connected-account password under the key the new main password derives,
    /// and returns that key for the cookie. A row that will not decrypt was already orphaned by an
    /// out-of-band password change: it is left exactly as it is, so the user can re-enter it.
    /// </summary>
    private async Task<byte[]> ReKeyConnectedAccountsAsync(
        SecretChange secretChange, CancellationToken cancellationToken)
    {
        var salt = await webmailUsers.GetOrCreateKdfSaltAsync(AuthenticatedUser.Email, cancellationToken);
        var newKek = ConnectedAccountCipher.DeriveKek(secretChange.NewPassword, salt);

        var accounts = await connectedAccounts.ListAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        if (accounts.Count == 0) return newKek;

        // A v1 cookie carries no key; the old password the request supplies derives it instead.
        var retrieved = credentials.Retrieve(Request);
        var oldKek = retrieved.IsSuccess && retrieved.Value.Kek is { } carried
            ? carried
            : ConnectedAccountCipher.DeriveKek(secretChange.OldPassword, salt);

        var reKeyed = new Dictionary<Guid, byte[]>(accounts.Count);
        foreach (var account in accounts)
        {
            // Same context on both halves: the row is not moving, only its key. A pre-binding
            // cipher comes out of this bound, so a password change migrates the whole set.
            var context = ConnectedAccountCipher.Context(account);
            var secret = ConnectedAccountCipher.Decrypt(oldKek, account.Cipher, context);
            if (secret.IsSuccess)
                reKeyed[account.Id] = ConnectedAccountCipher.Encrypt(newKek, secret.Value, context);
        }

        if (reKeyed.Count > 0)
            await connectedAccounts.ReplaceCiphersAsync(AuthenticatedUser.WebmailUid, reKeyed, cancellationToken);

        var orphaned = accounts.Count - reKeyed.Count;
        if (orphaned > 0)
            logger.LogWarning(
                "ChangeSecret left {OrphanedCount} connected accounts un-rekeyed: their cipher no longer decrypts",
                orphaned);

        return newKek;
    }

    /// <summary>
    /// Change the account full name
    /// </summary>
    /// <param name="fullNameChange">the new full name</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Full name changed successfully</response>
    /// <response code="400">Invalid request</response>
    /// <response code="401">Unauthenticated user</response>
    [HttpPost("FullName")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> ChangeFullName(FullNameChange fullNameChange, CancellationToken cancellationToken)
    {
        Result result = await usersRepository.ChangeFullNameAsync(AuthenticatedUser, fullNameChange.FullName, cancellationToken);
        return FromResult(result, successStatusCode: StatusCodes.Status204NoContent);
    }
}
