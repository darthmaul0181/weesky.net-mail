using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Scotty.Microservice.Models.WhatsNew;
using weesky.Scotty.Microservice.Repositories;

namespace weesky.Scotty.Microservice.Controllers;

/// <summary>
/// The caller's progress through the web app's novelties. The catalogue lives in the web app; this
/// side stores ids it has validated, three stamps per id and a baseline version written once.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class WhatsNewController(IWhatsNewStore store) : ApiBaseController
{
    /// <summary>The baseline and every novelty the account has a row for.</summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Baseline (absent until written) and stamps</response>
    /// <response code="401">Not authenticated</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<WhatsNewResponse>> GetWhatsNew(CancellationToken cancellationToken)
        => Ok(WhatsNewResponse.From(await store.GetAsync(AuthenticatedUser.WebmailUid, cancellationToken)));

    /// <summary>Stamps one state on a batch of novelties; a stamp already set is kept.</summary>
    /// <param name="request">ids (1 to 50) and the state to stamp</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Stamped</response>
    /// <response code="400">Missing ids or state, a malformed id, or too many ids</response>
    /// <response code="401">Not authenticated</response>
    [HttpPut("items")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> MarkItems(MarkWhatsNewRequest request, CancellationToken cancellationToken)
    {
        if (request?.Ids is null || request.State is not { } state || !Enum.IsDefined(state))
            return BadRequestEnveloppe("Ids and a known state are required");

        var ids = request.Ids.Distinct(StringComparer.Ordinal).ToList();
        if (WhatsNewRules.RefuseIds(ids) is { } refusal) return BadRequestEnveloppe(refusal);

        await store.MarkAsync(AuthenticatedUser.WebmailUid, ids, state, cancellationToken);
        return StatusCode(StatusCodes.Status204NoContent);
    }

    /// <summary>Writes the version from which novelties are signalled, once per account.</summary>
    /// <param name="request">the web app's version</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Written</response>
    /// <response code="400">Malformed version</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="409">The account already has a baseline</response>
    [HttpPut("baseline")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> SetBaseline(SetWhatsNewBaselineRequest request, CancellationToken cancellationToken)
    {
        if (!WhatsNewRules.IsVersion(request?.Version))
            return BadRequestEnveloppe("The version is major.minor.patch, optionally -dev");

        return await store.SetBaselineAsync(AuthenticatedUser.WebmailUid, request!.Version!, cancellationToken)
            ? StatusCode(StatusCodes.Status204NoContent)
            : ConflictEnveloppe("The baseline is already set");
    }
}
