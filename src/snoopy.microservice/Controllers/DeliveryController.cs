using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Delivery;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// The mail server's door (spec 5e3): Dovecot's Sieve script posts a guest's REPLY here at
/// delivery, with the mailbox owner and the shared key, and the reply is applied without a
/// session. Anonymous by design; the key is the authentication, and a wrong one is a 404, not a
/// 401 — the door must not say whether it is open (décision 7).
/// </summary>
[Route("api/Delivery")]
[ApiController]
[AllowAnonymous]
public sealed class DeliveryController(
    IDeliveryKeyProvider keys, IDeliveryKeyStore store, IWebmailUserStore users, IDeliveryReplyApplier applier,
    DeliveryRefusals refusals, TimeProvider clock, ILogger<DeliveryController> logger) : ApiBaseController
{
    internal const string KeyHeader = "X-Delivery-Key";
    internal const string MailboxHeader = "X-Delivery-Mailbox";
    internal const int MaxBodyBytes = 5 * 1024 * 1024;
    internal const string TooLarge = "too_large";

    /// <summary>Applies the calendar REPLY a raw mail carries into the mailbox owner's calendar.</summary>
    /// <response code="200">What was done: <c>{ outcome, uid?, detail? }</c></response>
    /// <response code="400"><c>X-Delivery-Mailbox</c> is not one address</response>
    /// <response code="404">The door is closed, or the key is wrong — indistinguishable on purpose</response>
    /// <response code="413">The mail is over 5 MB</response>
    [HttpPost("CalendarReplies")]
    [RequestSizeLimit(MaxBodyBytes)]
    [EnableRateLimiting(SecurityConfiguration.DeliveryPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<DeliveryReplyResponse>> ApplyCalendarReply(CancellationToken cancellationToken)
    {
        // Validated before anything is logged: the header is the one thing here that reaches the log.
        if (!DeliveryMailbox.TryNormalize(Request.Headers[MailboxHeader].ToString(), out var mailbox))
            return BadRequestEnveloppe($"{MailboxHeader} must be one e-mail address");

        var key = await keys.GetAsync(cancellationToken);
        if (key is null || !key.Enabled || !DeliveryKeys.Matches(Request.Headers[KeyHeader].ToString(), key.KeyHash))
        {
            if (refusals.Note() is { } count)
                logger.LogWarning("Delivery calls refused: {Count} since the last notice (door {State})",
                    count, key is { Enabled: true } ? "open, wrong key" : "closed");
            return NotFound();
        }
        await store.RecordCallAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);

        var account = await users.FindByEmailAsync(mailbox, cancellationToken);
        if (account is null) return Answer(new DeliveryReplyResponse(DeliveryReplyOutcome.UnknownMailbox, null, null), mailbox);

        var part = await DeliveryMailReader.ReadAsync(Request.Body, cancellationToken);
        var response = part.Status switch
        {
            DeliveryPartStatus.None => new DeliveryReplyResponse(DeliveryReplyOutcome.NotAReply, null, null),
            DeliveryPartStatus.TooLarge => new DeliveryReplyResponse(DeliveryReplyOutcome.NotApplicable, null, TooLarge),
            _ => await applier.ApplyAsync(new User(mailbox) { WebmailUid = account.Value.Id }, part.Ics!, cancellationToken),
        };
        return Answer(response, mailbox);
    }

    private ActionResult<DeliveryReplyResponse> Answer(DeliveryReplyResponse response, string mailbox)
    {
        var level = response.Outcome is DeliveryReplyOutcome.Conflict ? LogLevel.Warning : LogLevel.Information;
        logger.Log(level, "Delivery reply for {Mailbox}: {Outcome} {Uid} {Detail}",
            mailbox, response.Outcome, LogText.Safe(response.Uid), LogText.Safe(response.Detail));
        // The bare value, not Ok(response): ActionResult<T>'s implicit operator from ActionResult
        // (what Ok(...) returns) sets .Result, leaving .Value null — the same 200 on the wire, but
        // a caller reading .Value (as this controller's own tests do) would always see null.
        return response;
    }
}
