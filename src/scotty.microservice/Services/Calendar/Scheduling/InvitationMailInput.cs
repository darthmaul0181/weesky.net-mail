using weesky.Scotty.Microservice.Models.Calendar;

namespace weesky.Scotty.Microservice.Services.Calendar.Scheduling;

/// <summary>One organizer's mail to compose. <see cref="CancelSequence"/> is read only by a
/// cancellation: the stored SEQUENCE, plus one when the event itself is deleted (décision 11).</summary>
internal sealed record InvitationMailInput(
    MailKind Kind, string StoredIcs, int CancelSequence, IReadOnlyList<string> Recipients,
    OrganizerWrite Organizer, string Language, DateTime NowUtc);
