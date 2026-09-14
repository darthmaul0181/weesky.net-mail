using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>The invitation block as it now stands, and whether the answer reached the calendar;
/// <see cref="ApplyError"/> names why it did not — the card offers to retry (décision 12).</summary>
public sealed record ApplyReplyResponse(MailInvitation Invitation, bool Applied, string? ApplyError);
