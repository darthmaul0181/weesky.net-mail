using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// Answering an invitation received by mail (spec 5e, décision 4). A mail controller rather than a
/// calendar one: the file is re-read from the mailbox the request names, so the account is
/// resolved the way every api/Mail action resolves it.
/// </summary>
[ApiController]
[Route("api/Calendar/Invitations")]
[Authorize]
public sealed class CalendarInvitationsController(
    IInvitationResponder responder, IAccountConnectionResolver connections) : MailControllerBase(connections)
{
    /// <summary>Records the answer in the calendar and mails it to the organizer.</summary>
    /// <param name="request">the message, the part, the answer, and where a creation goes</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The invitation block as it now stands, with what was and was not sent</response>
    /// <response code="400">An answer foreign to the method, an invitation targeting one date of a series, or one not addressed to the user</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such message, or no such part on it</response>
    /// <response code="409">A newer version of the event is in the calendar, or the calendar holds the UID under another name</response>
    /// <response code="422">The part is not an invitation, or fails the guards every stored file passes</response>
    /// <response code="502">The mail server or the calendar could not be reached before the write</response>
    [HttpPost("Respond")]
    [ProducesResponseType(typeof(InvitationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<InvitationResponse>> Respond(RespondInvitationRequest request, CancellationToken cancellationToken)
    {
        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var result = await responder.RespondAsync(AuthenticatedUser, connection, request, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.Status, ResultEnveloppe.CreateErrorEnveloppe(result.Error.Message));
    }
}
