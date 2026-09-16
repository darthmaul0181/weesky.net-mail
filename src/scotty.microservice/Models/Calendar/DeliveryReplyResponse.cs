namespace weesky.Scotty.Microservice.Models.Calendar;

/// <summary>What the delivery door did with the mail (spec 5e3). <see cref="Detail"/> is the
/// <c>ReplyStatus</c> or <c>too_large</c> behind NotApplicable, the <c>DavWriteStatus</c> behind Conflict.</summary>
public sealed record DeliveryReplyResponse(DeliveryReplyOutcome Outcome, string? Uid, string? Detail);
