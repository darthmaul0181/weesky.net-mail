using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// The instance's settings — today, whether the webmail advertises itself as an installable
/// application, and under what name.
///
/// Reading is anonymous: an application name is not a secret, and the manifest must be reachable
/// from the login page, where there is no session. Writing is reserved to administrators. As with
/// the account preferences, the response always carries every known key with its default already
/// filled in, so the client keeps no copy of its own to drift from.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public sealed class AppSettingsController : ApiBaseController
{
    private readonly IAppSettingStore _store;

    public AppSettingsController(IAppSettingStore store)
    {
        _store = store;
    }

    /// <summary>Every known setting, with the stored value where one exists.</summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Key/value map covering every known setting</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyDictionary<string, string>>> GetAppSettings(
        CancellationToken cancellationToken)
    {
        var stored = await _store.GetAsync(cancellationToken);

        return Ok(AppSettings.Effective(stored));
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

        await _store.SetAsync(key, AppSettings.Normalize(key, value), cancellationToken);

        return StatusCode(StatusCodes.Status204NoContent);
    }
}
