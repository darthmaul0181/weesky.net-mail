using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// The caller's webmail preferences, stored in the separate preferences database.
///
/// The response always carries every known key: defaults live in the registry on this side, so
/// the client has no second copy to drift from. A key or value the registry does not accept is
/// refused here — the key/value table has no way to check either.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class PreferencesController : ApiBaseController
{
    private readonly IUserPreferenceStore _store;

    public PreferencesController(IUserPreferenceStore store)
    {
        _store = store;
    }

    /// <summary>Every known preference, with the account's value where it set one.</summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Key/value map covering every known preference</response>
    /// <response code="401">Not authenticated</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyDictionary<string, string>>> GetPreferences(CancellationToken cancellationToken)
    {
        var stored = await _store.GetAsync(AuthenticatedUser.WebmailUid, cancellationToken);

        return Ok(UserPreferences.Effective(stored));
    }

    /// <summary>Sets one preference.</summary>
    /// <param name="request">key and value, both from the registry</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Preference stored</response>
    /// <response code="400">Unknown key, or a value the key does not accept</response>
    /// <response code="401">Not authenticated</response>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> SetPreference(SetPreferenceRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));

        if (!UserPreferences.IsValid(request.Key ?? string.Empty, request.Value ?? string.Empty))
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(
                $"'{request.Value}' is not a value '{request.Key}' accepts"));

        await _store.SetAsync(AuthenticatedUser.WebmailUid, request.Key!, request.Value!, cancellationToken);

        return StatusCode(StatusCodes.Status204NoContent);
    }
}
