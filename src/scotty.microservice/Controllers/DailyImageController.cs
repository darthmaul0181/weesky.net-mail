using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services;

namespace weesky.Scotty.Microservice.Controllers;

/// <summary>
/// Bing's image of the day, served by the API so that browsers load nothing from elsewhere.
/// Anonymous: the login page shows it. Switched off, Bing is never asked.
/// </summary>
[Route("api/AppSettings/daily-image")]
[ApiController]
public sealed class DailyImageController(IAppSettingStore settings, IDailyImageService images) : ApiBaseController
{
    private const string NoImage = "No image of the day";

    /// <summary>The current image's version and credit.</summary>
    /// <param name="lang">interface language, en or fr: it picks Bing's market</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Version, title and copyright</response>
    /// <response code="404">Switched off, or Bing has no image to give</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DailyImageInfo>> GetDailyImage(
        [FromQuery] string? lang, CancellationToken cancellationToken)
    {
        var image = await CurrentAsync(lang, cancellationToken);
        if (image is null) return NotFoundEnveloppe(NoImage);

        Response.Headers.CacheControl = "no-cache";
        return Ok(new DailyImageInfo(image.Version, image.Title, image.Copyright));
    }

    /// <summary>The photo. Its URL carries the version, so it may be cached for good.</summary>
    /// <param name="version">the version the info route answered</param>
    /// <param name="lang">interface language, en or fr</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The JPEG</response>
    /// <response code="404">Switched off, no image, or a version that is no longer current</response>
    [HttpGet("{version}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetDailyImageFile(
        string version, [FromQuery] string? lang, CancellationToken cancellationToken)
    {
        var image = await CurrentAsync(lang, cancellationToken);
        if (image is null || image.Version != version) return NotFoundEnveloppe(NoImage);

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(image.Bytes, "image/jpeg");
    }

    private async Task<DailyImage?> CurrentAsync(string? lang, CancellationToken cancellationToken)
    {
        var effective = AppSettings.Effective(await settings.GetAsync(cancellationToken));
        if (effective[AppSettings.DailyImage] != "true") return null;

        return await images.GetAsync(BingArchive.MarketOf(lang), cancellationToken);
    }
}
