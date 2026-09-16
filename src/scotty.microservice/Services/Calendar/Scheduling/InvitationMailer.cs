using System.Text;
using MimeKit;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Services.Calendar.Invitations;

namespace weesky.Scotty.Microservice.Services.Calendar.Scheduling;

/// <summary>
/// Décision 11: a REQUEST carries the stored file, METHOD added and the reply stamps left out, twice — inline text/calendar
/// and an invitation.ics attachment, the double form Gmail, Outlook and Apple want before they
/// show their buttons; a CANCEL carries the reduced file the RFC asks for. Pure.
/// </summary>
internal static class InvitationMailer
{
    private const string Request = "REQUEST";

    internal static MimeMessage Compose(InvitationMailInput input)
    {
        var request = RequestOf(input.StoredIcs);
        var invitation = Parse(request);
        var calendarText = input.Kind == MailKind.Cancellation ? Cancel(input, invitation) : request;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(input.Organizer.Name ?? string.Empty, input.Organizer.Email));
        foreach (var recipient in input.Recipients)
            message.To.Add(Mailbox(recipient) ?? throw new ArgumentException("A recipient is not a deliverable address", nameof(input)));
        message.Date = new DateTimeOffset(DateTime.SpecifyKind(input.NowUtc, DateTimeKind.Utc));
        message.Subject = InvitationText.OrganizerSubject(input.Kind, invitation.Summary, input.Language);

        var (zone, named) = ZoneOf(invitation);
        var when = InvitationText.When(invitation, zone, input.Language) + (named is null ? string.Empty : $" ({named})");
        var body = InvitationText.OrganizerBody(input.Kind, invitation.Summary, when, invitation.Location,
            input.Organizer.Name ?? input.Organizer.Email, input.Language);
        var text = new TextPart("plain") { Text = body + "\r\n" };
        var calendar = new TextPart("calendar") { Text = calendarText };
        calendar.ContentType.Parameters.Add("method", input.Kind == MailKind.Cancellation ? "CANCEL" : Request);
        calendar.ContentType.Charset = "utf-8";
        var attachment = new MimePart("application", "ics")
        {
            Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(calendarText))),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = "invitation.ics",
        };
        message.Body = new Multipart("mixed") { new MultipartAlternative { text, calendar }, attachment };
        return message;
    }

    /// <summary>The one reading of a recipient, the scheduler's filter included: what MimeKit's constructor
    /// accepts, and only with an '@' — a bare word would pass it and fail at RCPT for the whole message.</summary>
    internal static MailboxAddress? Mailbox(string recipient)
    {
        try
        {
            var mailbox = new MailboxAddress(string.Empty, recipient);
            return mailbox.Address.Contains('@') ? mailbox : null;
        }
        catch (ParseException)
        {
            return null;
        }
    }

    internal static string Calendar(InvitationMailInput input)
    {
        var request = RequestOf(input.StoredIcs);
        return input.Kind == MailKind.Cancellation ? Cancel(input, Parse(request)) : request;
    }

    private static string RequestOf(string stored) => IcsMethod.With(PartStatRewriter.WithoutReplyStamp(stored), Request);

    private static ParsedInvitation Parse(string request) =>
        InvitationParser.Read(request).Invitation ?? throw new InvalidOperationException("The stored file does not read as an event");

    /// <summary>RFC 5546 § 3.2.5: the ATTENDEEs it concerns, and the DTSTART line some agendas pair on.</summary>
    private static string Cancel(InvitationMailInput input, ParsedInvitation e) => ItipCalendar.Reduced(
        method: "CANCEL", uid: e.Uid, sequence: input.CancelSequence, nowUtc: input.NowUtc,
        organizerEmail: input.Organizer.Email, organizerName: input.Organizer.Name,
        attendees: input.Recipients.Select(r => (r, (string?)null, (string?)null)),
        dtStartLine: e.DtStartLine, summary: e.Summary, status: "CANCELLED");

    /// <summary>The zone the time is read in (« dans le fuseau de l'événement ») and the name the text gives
    /// it, so a guest elsewhere knows whose clock it is: the IANA id its TZID resolves to, UTC for a UTC time or
    /// a TZID nothing resolves (the instant is still right, read through the file's own block); no name for a
    /// floating time or a whole day, which every guest reads as their own.</summary>
    private static (string Zone, string? Named) ZoneOf(ParsedInvitation e)
    {
        if (e.IsAllDay || e.DtStartLine is not { } line) return (IcsTimeZones.Utc, null);
        if (IcsGuards.TzIdsOf(line).FirstOrDefault() is { } tzid)
        {
            var zone = IcsTimeZones.ResolveIana(tzid) ?? IcsTimeZones.Utc;
            return (zone, zone);
        }
        return (IcsTimeZones.Utc, line.TrimEnd().EndsWith('Z') ? IcsTimeZones.Utc : null);
    }
}
