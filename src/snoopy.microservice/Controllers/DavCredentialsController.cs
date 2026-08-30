using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// The Sync settings tab, and nothing else: the three values a CardDAV client asks for, one switch
/// per protocol, and a regeneration. No reveal — the table holds a digest, and a screen able to
/// show the secret again would force it to hold the secret itself.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class DavCredentialsController(
    IDavCredentialStore store,
    IDavAuthenticationCache cache,
    IAuthAttemptThrottle throttle,
    IOptions<DavOptions> davOptions,
    ILogger<DavCredentialsController> logger) : ApiBaseController
{
    private const string NotServed = "Synchronisation is not available on this deployment";

    /// <summary>The spelling the cache is keyed on, and the one the screen tells the user to type;
    /// the cache compares byte for byte, so a Forget under another casing revokes nothing.</summary>
    private string Identifier => IdentityResolver.Canonical(AuthenticatedUser.Email);

    /// <summary>
    /// Returns the synchronisation state
    /// </summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The state — never a secret, in any shape</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">This deployment publishes no synchronisation address</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DavCredentialsView>> Get(CancellationToken cancellationToken)
    {
        if (!davOptions.Value.IsConfigured) return NotFoundEnveloppe(NotServed);

        return Ok(await ViewAsync(secret: null, cancellationToken));
    }

    /// <summary>
    /// Turns contact synchronisation on or off
    /// </summary>
    /// <remarks>
    /// Turning it on for the first time creates the credentials and returns the secret **in this
    /// same response** — the one and only moment it exists in clear. Turning it back on returns
    /// none: there is nothing new to show, and every configured device keeps working. Turning it
    /// off destroys nothing.
    /// </remarks>
    /// <param name="toggle">the wanted state of the CardDAV switch</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The new state, carrying the secret only when this call drew one</response>
    /// <response code="400">A body that names no state — it is refused, never read as a switch-off</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">This deployment publishes no synchronisation address</response>
    [HttpPut("CardDav")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DavCredentialsView>> SetCardDav(
        DavSyncToggle toggle, CancellationToken cancellationToken)
    {
        if (!davOptions.Value.IsConfigured) return NotFoundEnveloppe(NotServed);

        string? secret = null;
        if (toggle.Enabled)
        {
            secret = await store.EnableAsync(AuthenticatedUser.WebmailUid, cancellationToken);
            // Same reason as the regeneration below: enabling for the first time mints a secret and
            // lands every configured device in the failure loop that blocks the identifier.
            throttle.ForgetIdentifier(Identifier);
        }
        else
        {
            await store.DisableAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        }

        // The cached entry carries the switch state, so it answers with the old one for the rest
        // of the window — in both directions: a 200 after switching off, a 403 after switching on.
        cache.Forget(Identifier);

        logger.LogInformation(
            "Audit: carddav_sync user={UserId} enabled={Enabled} created={Created} outcome=success",
            AuthenticatedUser.WebmailUid, toggle.Enabled, secret is not null);

        return Ok(await ViewAsync(secret, cancellationToken));
    }

    /// <summary>
    /// Draws a new synchronisation secret
    /// </summary>
    /// <remarks>
    /// Every device stops syncing until the new one is entered. The screen says so before asking.
    /// </remarks>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The new state, carrying the new secret</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">Synchronisation was never enabled, or this deployment publishes no address</response>
    [HttpPost("Regenerate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DavCredentialsView>> Regenerate(CancellationToken cancellationToken)
    {
        if (!davOptions.Value.IsConfigured) return NotFoundEnveloppe(NotServed);

        var secret = await store.RegenerateAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        // Regenerating what was never enabled is not a create: the switch is the only door in.
        if (secret is null) return NotFoundEnveloppe("Synchronisation has never been enabled");

        cache.Forget(Identifier);
        // The regeneration itself is what put every device in a failure loop, and ten failures on
        // the identifier answer 429 to the correct new secret. The JWT this call carries is a
        // factor the throttle does not guard; the address key stays, so a neighbour on the same
        // /64 gains nothing.
        throttle.ForgetIdentifier(Identifier);
        logger.LogInformation("Audit: carddav_regenerate user={UserId} outcome=success",
            AuthenticatedUser.WebmailUid);

        return Ok(await ViewAsync(secret, cancellationToken));
    }

    private async Task<DavCredentialsView> ViewAsync(string? secret, CancellationToken cancellationToken)
    {
        var state = await store.GetStateAsync(AuthenticatedUser.WebmailUid, cancellationToken);

        return new DavCredentialsView(
            davOptions.Value.PublicUrl!,
            Identifier,
            state.Configured,
            state.CardDavEnabled,
            state.LastUsedAt,
            secret);
    }
}
