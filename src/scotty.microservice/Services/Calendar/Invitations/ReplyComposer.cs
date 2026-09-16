using MimeKit;

namespace weesky.Scotty.Microservice.Services.Calendar.Invitations;

internal sealed record ReplyInput(
    string FromAddress, string? FromName, string OrganizerEmail, string? OrganizerName,
    ParsedInvitation Invitation, string PartStat, string TimeZone, string Language, DateTime NowUtc);

/// <summary>
/// The REPLY mail (décision 5): a one-line text and a reduced VCALENDAR — METHOD, the UID, the
/// SEQUENCE received, DTSTAMP, ORGANIZER, the user's ATTENDEE alone, and the file's own DTSTART
/// line, which some agendas need to pair the answer. The shape Thunderbird and Google produce.
/// </summary>
internal static class ReplyComposer
{
    internal static MimeMessage Compose(ReplyInput input)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(input.FromName ?? string.Empty, input.FromAddress));
        message.To.Add(new MailboxAddress(input.OrganizerName ?? string.Empty, input.OrganizerEmail));
        message.Date = new DateTimeOffset(DateTime.SpecifyKind(input.NowUtc, DateTimeKind.Utc));
        message.Subject = InvitationText.Subject(input.PartStat, input.Invitation.Summary, input.Language);

        var who = string.IsNullOrWhiteSpace(input.FromName) ? input.FromAddress : input.FromName;
        var when = InvitationText.When(input.Invitation, input.TimeZone, input.Language);
        var text = new TextPart("plain") { Text = InvitationText.Body(who, input.PartStat, input.Invitation.Summary, when, input.Language) + "\r\n" };
        var calendar = new TextPart("calendar") { Text = Calendar(input) };
        calendar.ContentType.Parameters.Add("method", "REPLY");
        calendar.ContentType.Charset = "utf-8";
        message.Body = new MultipartAlternative { text, calendar };
        return message;
    }

    internal static string Calendar(ReplyInput input) => ItipCalendar.Reduced(
        method: "REPLY", uid: input.Invitation.Uid, sequence: input.Invitation.Sequence, nowUtc: input.NowUtc,
        organizerEmail: input.OrganizerEmail, organizerName: input.OrganizerName,
        attendees: [(input.FromAddress, input.FromName, input.PartStat)],
        dtStartLine: input.Invitation.DtStartLine, summary: input.Invitation.Summary);
}
