using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class AccountController(
    IAccountInfoProvider accountInfo,
    IAccountConnectionResolver connections,
    IImapSessionProvider imapSessions) : ApiBaseController
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
        Result<AccountInfo> result = await accountInfo.GetAccountInfoAsync(AuthenticatedUser, cancellationToken);
        return FromResult(result, errorStatusCode: StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Returns the mailbox quota usage, read live over IMAP GETQUOTAROOT INBOX
    /// </summary>
    /// <response code="200">Quota information</response>
    /// <response code="204">The mail server does not advertise the QUOTA capability</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">Unable to reach the mail server</response>
    [HttpGet("Quota")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<Quota>> GetQuota(CancellationToken cancellationToken)
    {
        var resolved = await connections.ResolveAsync(AuthenticatedUser, Request, cancellationToken);
        if (resolved.IsFailure) return ConnectedAccountError(resolved.Error);

        var session = await imapSessions.GetAsync(resolved.Value, cancellationToken);
        if (session.IsFailure) return BadGatewayEnveloppe(session.Error);

        if (!session.Value.SupportsQuota) return NoContent();

        Result<Quota> result = await session.Value.GetQuotaAsync(cancellationToken);
        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }
}
