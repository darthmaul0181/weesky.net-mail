using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using weesky.Scotty.Microservice.Configuration;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services;

namespace weesky.Scotty.Microservice.Controllers;

/// <summary>
/// A sender's picture, fetched by the server so the browser never contacts Gravatar or the sender's domain:
/// the Gravatar of an address, or the BIMI logo of a domain the caller's list has already DMARC-gated.
/// Both routes answer 404 before any lookup while the caller's <c>mail.showAvatars</c> is off: no address
/// or domain leaves the server unless its reader opted in.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
[EnableRateLimiting(SecurityConfiguration.AvatarsPolicy)]
public sealed class AvatarsController(IUserPreferenceStore preferences, IAvatarService avatars, IBimiLogoService bimi) : ApiBaseController
{
    private const string NoAvatar = "No avatar";

    /// <summary>The picture behind one address.</summary>
    /// <param name="request">the bare sender address</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The image, served with its own content type</response>
    /// <response code="400">Not a bare address, or longer than 254 characters</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">Avatars are off for the caller, or the address has no picture</response>
    /// <response code="429">Too many lookups in the last minute</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult> GetAvatar([FromQuery] AvatarRequest request, CancellationToken cancellationToken)
    {
        if (await AvatarsAreOffAsync(cancellationToken)) return NotFoundEnveloppe(NoAvatar);
        if (!request.IsBareAddress) return BadRequestEnveloppe("A single bare email address is required");

        return ImageOrNotFound(await avatars.GetAsync(request.Address!, cancellationToken));
    }

    /// <summary>The BIMI logo a sender's domain publishes, for a domain the list found DMARC-aligned.</summary>
    /// <param name="request">the sender domain and optional BIMI selector, normalised before the lookup</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The logo, rasterised to a PNG</response>
    /// <response code="400">Not a domain name, or not a BIMI selector</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">Avatars are off for the caller, or the domain has no usable logo</response>
    /// <response code="429">Too many lookups in the last minute</response>
    [HttpGet("Bimi")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult> GetBimiLogo([FromQuery] BimiRequest request, CancellationToken cancellationToken)
    {
        if (await AvatarsAreOffAsync(cancellationToken)) return NotFoundEnveloppe(NoAvatar);
        if (!request.IsValid) return BadRequestEnveloppe("A domain name and a BIMI selector are required");

        return ImageOrNotFound(await bimi.GetAsync(request.DomainName, request.SelectorName, cancellationToken));
    }

    private async Task<bool> AvatarsAreOffAsync(CancellationToken cancellationToken) =>
        UserPreferences.Effective(await preferences.GetAsync(AuthenticatedUser.WebmailUid, cancellationToken))[UserPreferences.MailShowAvatars] != "true";

    private ActionResult ImageOrNotFound(AvatarImage? image)
    {
        if (image is null) return NotFoundEnveloppe(NoAvatar);

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(image.Bytes, image.ContentType);
    }
}
