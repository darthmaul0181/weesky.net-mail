using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Scotty.Microservice.Authentication.Authorization;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Repositories;

namespace weesky.Scotty.Microservice.Controllers;

/// <summary>
/// The instance's settings — today, whether the webmail advertises itself as an installable
/// application, and under what name and logo.
///
/// Reading is anonymous: an application name is not a secret, and the manifest must be reachable
/// from the login page, where there is no session. Writing is reserved to administrators. As with
/// the account preferences, the response always carries every known key with its default already
/// filled in, so the client keeps no copy of its own to drift from.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public sealed class AppSettingsController(IAppSettingStore store, IAppLogoStore logos) : ApiBaseController
{
    private const string NoLogo = "No logo of that size";

    /// <summary>Every known setting, with the stored value where one exists.</summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Key/value map covering every known setting, plus <c>app.logo</c>, the logo's version or empty</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyDictionary<string, string>>> GetAppSettings(
        CancellationToken cancellationToken)
    {
        var effective = new Dictionary<string, string>(AppSettings.Effective(await store.GetAsync(cancellationToken)))
        {
            [AppLogo.VersionKey] = await logos.GetUpdatedAtAsync(cancellationToken) is { } at
                ? AppLogo.Version(at) : string.Empty,
        };

        return Ok(effective);
    }

    /// <summary>Sets one setting.</summary>
    /// <param name="request">key and value, both from the registry</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Setting stored</response>
    /// <response code="400">Unknown key, or a value the key does not accept</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    [HttpPut]
    [Authorize(Policy = AdminRequirement.PolicyName)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> SetAppSetting(
        SetAppSettingRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");

        var key = request.Key ?? string.Empty;
        var value = request.Value ?? string.Empty;

        if (!AppSettings.IsValid(key, value))
            return BadRequestEnveloppe($"'{value}' is not a value '{key}' accepts");

        await store.SetAsync(key, AppSettings.Normalize(key, value), cancellationToken);

        return StatusCode(StatusCodes.Status204NoContent);
    }

    /// <summary>One rendition of the instance logo. Its URL carries the version, so it may be cached for good.</summary>
    /// <param name="size">32, 192 or 512</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The PNG</response>
    /// <response code="404">An unknown size, or no logo stored (the client then uses the bundled one)</response>
    [HttpGet("logo/{size:int}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetLogo(int size, CancellationToken cancellationToken)
    {
        var image = AppLogo.Sizes.Contains(size) ? await logos.GetImageAsync(size, cancellationToken) : null;
        if (image is null) return NotFoundEnveloppe(NoLogo);

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(image, "image/png");
    }

    /// <summary>Replaces the instance logo: the three renditions together, or none of them.</summary>
    /// <param name="logo32">the 32×32 PNG</param>
    /// <param name="logo192">the 192×192 PNG</param>
    /// <param name="logo512">the 512×512 PNG</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Logo stored</response>
    /// <response code="400">A rendition missing, not a PNG, of the wrong size or too heavy</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    [HttpPut("logo")]
    [Authorize(Policy = AdminRequirement.PolicyName)]
    [RequestSizeLimit(AppLogo.MaxRequestBytes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> SetLogo(
        IFormFile? logo32, IFormFile? logo192, IFormFile? logo512, CancellationToken cancellationToken)
    {
        var images = new Dictionary<int, byte[]>();
        foreach (var (size, file) in new[] { (32, logo32), (192, logo192), (512, logo512) })
        {
            if (file is null) return BadRequestEnveloppe($"The {size} px logo is missing");
            if (file.Length > AppLogo.MaxBytes(size))
                return BadRequestEnveloppe($"The {size} px logo is over {AppLogo.MaxBytes(size) / 1024} KB");

            using var buffer = new MemoryStream((int)file.Length);
            await file.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();

            var check = AppLogo.Check(size, bytes);
            if (check.IsFailure) return BadRequestEnveloppe(check.Error);
            images[size] = bytes;
        }

        await logos.ReplaceAsync(images, cancellationToken);
        return StatusCode(StatusCodes.Status204NoContent);
    }

    /// <summary>Back to the bundled Scotty logo.</summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Logo removed</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    [HttpDelete("logo")]
    [Authorize(Policy = AdminRequirement.PolicyName)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> DeleteLogo(CancellationToken cancellationToken)
    {
        await logos.ClearAsync(cancellationToken);
        return StatusCode(StatusCodes.Status204NoContent);
    }
}
