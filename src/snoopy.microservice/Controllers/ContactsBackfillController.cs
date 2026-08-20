using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// The one-shot operator route of slice 4a: it gives a card, a hash and a projection to every
/// contact stored before the vCard model existed. Its own controller rather than an action of
/// <see cref="ContactsController"/>, whose class-level <c>[Authorize]</c> says "any signed-in
/// user, their own book only" — the exact opposite of what this route is. It shares that
/// controller's prefix by an explicit route, for the same reason the four <c>api/Mail</c>
/// controllers do: <c>[controller]</c> would move the URL.
/// </summary>
[Route("api/Contacts")]
[ApiController]
public sealed class ContactsBackfillController(
    IContactStore store, ILogger<ContactsBackfillController> logger) : ApiBaseController
{
    /// <summary>Contacts converted per call when the operator names no size.</summary>
    internal const int DefaultBatchSize = 200;

    /// <summary>
    /// What one call may convert. The pass rewrites a card and re-projects four child tables per
    /// contact, all in one transaction, so the ceiling is what keeps a batch inside an HTTP
    /// request rather than a number of rows the table cares about.
    /// </summary>
    internal const int MaxBatchSize = 1000;

    /// <summary>
    /// Converts one batch and says how many contacts are still waiting. Idempotent: the queue is
    /// the contacts whose <c>card_hash</c> is still empty, so a replay converts nothing and
    /// answers zero. Call it again while <c>remaining</c> is above zero.
    /// </summary>
    /// <param name="batchSize">contacts to convert in this call; clamped to 1..1000</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Converted and remaining counts</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    [HttpPost("Backfill")]
    [Authorize(Policy = AdminRequirement.PolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BackfillOutcome>> Backfill(
        [FromQuery] int? batchSize, CancellationToken cancellationToken)
    {
        // Clamped rather than refused: an operator running this at 2am wants work done, not a 400.
        var outcome = await store.BackfillAsync(
            Math.Clamp(batchSize ?? DefaultBatchSize, 1, MaxBatchSize), cancellationToken);

        logger.LogInformation("Contacts backfill: {Processed} processed, {Remaining} remaining",
            outcome.Processed, outcome.Remaining);
        return Ok(outcome);
    }
}
