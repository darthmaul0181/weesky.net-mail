using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// What the frontend must gate on, in one authenticated call: what the platform wires versus what
/// the mail servers behind this account actually support. Quota needs the primary mailbox's live
/// IMAP session — the same resolution every mail endpoint runs — so a missing or undecryptable
/// credentials cookie answers exactly as it does there, never a partial 200.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class CapabilitiesController(
    IOptions<PlatformOptions> platformOptions,
    IOptions<SieveOptions> sieveOptions,
    IOptions<DavOptions> davOptions,
    IAliasDirectory aliasDirectory,
    IAccountInfoProvider accountInfo,
    IAccountConnectionResolver connections,
    IImapSessionProvider imapSessions,
    ISieveAvailabilityProbe sieveProbe) : ApiBaseController
{
    /// <summary>
    /// Returns what the platform wires and the servers support
    /// </summary>
    /// <response code="200">Capabilities</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such account</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">Unable to reach the mail server</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<CapabilitiesResponse>> GetCapabilities(CancellationToken cancellationToken)
    {
        var resolved = await connections.ResolveAsync(AuthenticatedUser, Request, cancellationToken);
        if (resolved.IsFailure) return ConnectedAccountError(resolved.Error);

        var session = await imapSessions.GetAsync(resolved.Value, cancellationToken);
        if (session.IsFailure) return BadGatewayEnveloppe(session.Error);

        var isWeesky = platformOptions.Value.IsWeesky;

        var rules = await sieveProbe.IsAvailableAsync(sieveOptions.Value.Host, sieveOptions.Value.Port, cancellationToken);

        return Ok(new CapabilitiesResponse(
            Platform: isWeesky ? PlatformOptions.Weesky : PlatformOptions.Generic,
            Admin: isWeesky && await IsAdminAsync(cancellationToken),
            Aliases: isWeesky,
            PasswordChange: isWeesky,
            ProfileEditing: isWeesky,
            StrictIdentities: aliasDirectory.EnforcesOwnership,
            Quota: session.Value.SupportsQuota,
            Rules: rules,
            // Configured means served: a deployment with no published address has no /dav, and the
            // Sync tab must not be a dead row on its settings screen.
            Dav: davOptions.Value.IsConfigured));
    }

    /// <summary>A failed account lookup means "not admin", not an error for the whole response —
    /// the caller already learned everything wrong about their session from the resolution above.</summary>
    private async Task<bool> IsAdminAsync(CancellationToken cancellationToken)
    {
        var result = await accountInfo.GetAccountInfoAsync(AuthenticatedUser, cancellationToken);
        return result.IsSuccess && result.Value.IsAdmin;
    }
}
