using System.Globalization;
using System.Text;
using MimeKit;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

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
    private const string ProductId = "-//weesky//webmail//EN";

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

    internal static string Calendar(ReplyInput input)
    {
        var e = input.Invitation;
        var ics = new StringBuilder()
            .Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:").Append(ProductId).Append("\r\nMETHOD:REPLY\r\n")
            .Append("BEGIN:VEVENT\r\nUID:").Append(e.Uid).Append("\r\n")
            .Append("SEQUENCE:").Append(e.Sequence).Append("\r\n")
            .Append("DTSTAMP:").Append(input.NowUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)).Append("\r\n")
            .Append("ORGANIZER").Append(Cn(input.OrganizerName)).Append(":mailto:").Append(input.OrganizerEmail).Append("\r\n")
            .Append("ATTENDEE;PARTSTAT=").Append(input.PartStat).Append(Cn(input.FromName)).Append(":mailto:").Append(input.FromAddress).Append("\r\n");
        if (e.DtStartLine is { } dtstart) ics.Append(dtstart).Append("\r\n");
        if (!string.IsNullOrWhiteSpace(e.Summary)) ics.Append("SUMMARY:").Append(EscapeText(e.Summary)).Append("\r\n");
        return ics.Append("END:VEVENT\r\nEND:VCALENDAR\r\n").ToString();
    }

    /// <summary>RFC 5545 § 3.2: a parameter value holding ':' ';' or ',' is quoted; a '"' cannot appear in one.
    /// The line breaks go with it: the name is the user's own profile, and one would splice a line
    /// of its choosing into the REPLY.</summary>
    private static string Cn(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var clean = name.Replace("\"", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        return ";CN=" + (clean.IndexOfAny([':', ';', ',']) >= 0 ? $"\"{clean}\"" : clean);
    }

    /// <summary>RFC 5545 § 3.3.11.</summary>
    private static string EscapeText(string value) =>
        value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");
}
