using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// Senders whose remote images this account loads without asking — a webmail preference, not
/// mail-server data, so no IMAP session and no credentials cookie. The reader tests the list
/// itself; the sanitiser is never told about it, which is what keeps one message body good for
/// every account.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class TrustedSendersController(ITrustedSenderStore store) : ApiBaseController
{
    /// <summary>The approved addresses, canonical and sorted.</summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The addresses</response>
    /// <response code="401">Not authenticated</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<string>>> List(CancellationToken cancellationToken)
        => Ok(await store.ListAsync(AuthenticatedUser.WebmailUid, cancellationToken));

    /// <summary>Starts trusting one sender. Approving an address already stored is not an error.</summary>
    /// <param name="request">the address to trust</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Trusted</response>
    /// <response code="400">The address does not parse, or the account is at its ceiling</response>
    /// <response code="401">Not authenticated</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> Add(TrustedSenderRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Address))
            return BadRequestEnveloppe("An address is required");

        // Not "that is not a valid email address": the caller typed nothing, they picked a menu
        // entry about a sender, so the fault is in the message's header, not in their input.
        if (!MailboxAddress.TryParse(RecipientAddressParser.Options, request.Address, out var mailbox))
            return BadRequestEnveloppe("That sender's address could not be read");

        // A decorated form would store a row no message's bare FromAddress could ever match.
        var result = await store.AddAsync(AuthenticatedUser.WebmailUid, mailbox.Address, cancellationToken);
        return result.IsFailure ? BadRequestEnveloppe(result.Error) : NoContent();
    }

    /// <summary>
    /// Stops trusting one sender. Always 204, unknown address included: a 404 would confirm
    /// which addresses this account has approved, and the caller gains nothing from the answer.
    /// </summary>
    /// <param name="address">the address to stop trusting</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">No longer trusted, whether it was or not</response>
    /// <response code="401">Not authenticated</response>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> Remove([FromQuery] string address, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(address))
            await store.RemoveAsync(AuthenticatedUser.WebmailUid, address, cancellationToken);

        return NoContent();
    }
}
