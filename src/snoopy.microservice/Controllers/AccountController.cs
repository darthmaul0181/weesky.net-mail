using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Authentication.Models;
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
    IOptions<TokenConstants> tokenConstants) : ApiBaseController
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
    public async Task<ActionResult<AccountInfo>> GetAccountInfo()
    {
        Result<AccountInfo> result = await usersRepository.GetAccountInfoAsync(AuthenticatedUser);
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
    /// </remarks>
    /// <param name="secretChange">the new secret</param>
    /// <response code="204">Secret changed successfully</response>
    /// <response code="400">Wrong credentials</response>
    /// <response code="401">Unauthenticated user</response>
    [HttpPatch("ChangeSecret")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> ChangePassword(SecretChange secretChange)
    {
        Result result = await usersRepository.ChangePasswordAsync(AuthenticatedUser, secretChange.NewPassword, secretChange.OldPassword);

        if (result.IsSuccess)
        {
            credentials.Store(Response, secretChange.NewPassword,
                TimeSpan.FromMinutes(tokenConstants.Value.ExpiryInMinutes));
        }

        return FromResult(result, successStatusCode: StatusCodes.Status204NoContent);
    }

    /// <summary>
    /// Change the account full name
    /// </summary>
    /// <param name="fullNameChange">the new full name</param>
    /// <response code="204">Full name changed successfully</response>
    /// <response code="400">Invalid request</response>
    /// <response code="401">Unauthenticated user</response>
    [HttpPost("FullName")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> ChangeFullName(FullNameChange fullNameChange)
    {
        Result result = await usersRepository.ChangeFullNameAsync(AuthenticatedUser, fullNameChange.FullName);
        return FromResult(result, successStatusCode: StatusCodes.Status204NoContent);
    }
}
