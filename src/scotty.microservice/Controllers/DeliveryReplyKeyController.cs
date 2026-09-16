using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Scotty.Microservice.Authentication.Authorization;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services.Calendar.Delivery;

namespace weesky.Scotty.Microservice.Controllers;

/// <summary>The key the mail server presents at <c>POST /api/Delivery/CalendarReplies</c>, and the
/// switch that opens that door (spec 5e3). Admin-only; the key is shown once, at generation.</summary>
[Route("api/[controller]")]
[ApiController]
[Authorize(Policy = AdminRequirement.PolicyName)]
public sealed class DeliveryReplyKeyController(IDeliveryKeyStore store, IDeliveryKeyProvider keys, TimeProvider clock)
    : ApiBaseController
{
    internal const string KeyMissing = "delivery_key_missing";
    internal const string ChangedConcurrently = "delivery_key_changed_concurrently";

    /// <summary>Whether a key exists, the switch, and the dates the card shows. Never the key.</summary>
    /// <response code="200">The state</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DeliveryReplyKeyResponse>> GetDeliveryReplyKey(CancellationToken cancellationToken)
    {
        var row = await store.FindAsync(cancellationToken);
        return row is null ? DeliveryReplyKeyResponse.None
            : new DeliveryReplyKeyResponse(true, row.Enabled, Utc(row.CreatedAt), row.LastCallAt is { } at ? Utc(at) : null);
    }

    /// <summary>Generates the key, or replaces it — the previous one is refused from this answer on.
    /// The key is returned once and never stored; the last-call date starts over.</summary>
    /// <response code="200"><c>{ key }</c>, once</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    /// <response code="409">Concurrent writes won twice (<c>delivery_key_changed_concurrently</c>)</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeliveryReplyKeyGenerated>> GenerateDeliveryReplyKey(CancellationToken cancellationToken)
    {
        var key = DeliveryKeys.Generate();
        if (!await store.ReplaceAsync(DeliveryKeys.Hash(key), clock.GetUtcNow().UtcDateTime, cancellationToken))
            return ConflictEnveloppe(ChangedConcurrently);
        await keys.InvalidateAsync(CancellationToken.None);
        Response.Headers.CacheControl = "no-store";
        return Ok(new DeliveryReplyKeyGenerated(key));
    }

    /// <summary>Opens or closes the door. Opening needs a key.</summary>
    /// <response code="204">Switched</response>
    /// <response code="400">No request body</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    /// <response code="409">Enabling with no key stored (<c>delivery_key_missing</c>), or concurrent writes won twice</response>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> SetDeliveryReplies(DeliveryReplyKeyEnableRequest request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequestEnveloppe("Request body is required");
        var write = await store.SetEnabledAsync(request.Enabled, clock.GetUtcNow().UtcDateTime, cancellationToken);
        if (write is DeliveryKeyWrite.NoKey) return request.Enabled ? ConflictEnveloppe(KeyMissing) : NoContent();
        if (write is DeliveryKeyWrite.Conflict) return ConflictEnveloppe(ChangedConcurrently);
        await keys.InvalidateAsync(CancellationToken.None);
        return NoContent();
    }

    /// <summary>Removes the key and closes the door.</summary>
    /// <response code="204">Removed</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    /// <response code="404">No key stored (<c>delivery_key_missing</c>)</response>
    /// <response code="409">Concurrent writes won twice</response>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> DeleteDeliveryReplyKey(CancellationToken cancellationToken)
    {
        var write = await store.DeleteAsync(cancellationToken);
        if (write is DeliveryKeyWrite.NoKey) return NotFoundEnveloppe(KeyMissing);
        if (write is DeliveryKeyWrite.Conflict) return ConflictEnveloppe(ChangedConcurrently);
        await keys.InvalidateAsync(CancellationToken.None);
        return NoContent();
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
