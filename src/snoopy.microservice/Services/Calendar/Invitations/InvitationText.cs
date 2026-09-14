using System.Globalization;
using NodaTime;
using weesky.Snoopy.Microservice.Models.Calendar;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

/// <summary>The words of a REPLY and of the organizer's mails, in the two languages the webmail
/// speaks. French carries its non-breaking spaces (U+00A0) before ':' and inside « ». Anything but
/// "fr" is English.</summary>
internal static class InvitationText
{
    private const string Nbsp = "\u00A0";

    internal static string When(ParsedInvitation invitation, string timeZone, string language)
    {
        var culture = Culture(language);
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZone) ?? DateTimeZone.Utc;
        if (invitation.IsAllDay && invitation.StartDate is { } day)
        {
            var first = day.ToString("dddd d MMMM yyyy", culture);
            // Half-open range: the last day covered is the day before the exclusive end.
            if (invitation.EndDateExclusive is { } end && end.DayNumber > day.DayNumber + 1)
            {
                var last = end.AddDays(-1).ToString("dddd d MMMM yyyy", culture);
                return IsFrench(language) ? $"{first} au {last}" : $"{first} \u2013 {last}";
            }
            return first + (IsFrench(language) ? ", journ\u00e9e enti\u00e8re" : ", all day");
        }
        if (invitation.Start is not { } start) return string.Empty;
        var local = Instant.FromDateTimeUtc(DateTime.SpecifyKind(start, DateTimeKind.Utc)).InZone(zone).LocalDateTime;
        return local.ToDateTimeUnspecified().ToString("dddd d MMMM yyyy, HH:mm", culture);
    }

    internal static string Subject(string partStat, string? summary, string language)
    {
        var title = Title(summary, language);
        var word = (IsFrench(language), partStat) switch
        {
            (true, "ACCEPTED") => "Accept\u00e9" + Nbsp + ":",
            (true, "TENTATIVE") => "Provisoire" + Nbsp + ":",
            (true, _) => "Refus\u00e9" + Nbsp + ":",
            (false, "ACCEPTED") => "Accepted:",
            (false, "TENTATIVE") => "Tentative:",
            (false, _) => "Declined:",
        };
        return word + " " + title;
    }

    internal static string Body(string who, string partStat, string? summary, string when, string language)
    {
        var title = Title(summary, language);
        if (IsFrench(language))
        {
            var verb = partStat switch
            {
                "ACCEPTED" => "a accept\u00e9", "TENTATIVE" => "a r\u00e9pondu peut-\u00eatre \u00e0", _ => "a refus\u00e9",
            };
            return $"{who} {verb} l'invitation \u00ab{Nbsp}{title}{Nbsp}\u00bb du {when}.";
        }
        var english = partStat switch
        {
            "ACCEPTED" => "has accepted", "TENTATIVE" => "has tentatively accepted", _ => "has declined",
        };
        return $"{who} {english} the invitation \u201C{title}\u201D on {when}.";
    }

    internal static string OrganizerSubject(MailKind kind, string? summary, string language)
    {
        var fr = IsFrench(language);
        var head = (kind, fr) switch
        {
            (MailKind.Invitation, _) => "Invitation",
            (MailKind.Update, true) => "Mise \u00e0 jour",
            (MailKind.Update, false) => "Updated",
            (_, true) => "Annulation",
            (_, false) => "Cancelled",
        };
        return head + (fr ? Nbsp + ": " : ": ") + Title(summary, language);
    }

    /// <summary>The organizer's plain text (décision 11): title, when, where, by whom, then how to answer.</summary>
    internal static string OrganizerBody(MailKind kind, string? summary, string when, string? location, string organizer, string language)
    {
        var fr = IsFrench(language);
        List<string> lines = [Title(summary, language), (fr ? "Quand" + Nbsp + ": " : "When: ") + when];
        if (!string.IsNullOrWhiteSpace(location)) lines.Add((fr ? "O\u00f9" + Nbsp + ": " : "Where: ") + OneLine(location));
        lines.Add((fr ? "Organis\u00e9 par " : "Organised by ") + organizer);
        lines.Add(string.Empty);
        lines.Add((kind, fr) switch
        {
            (MailKind.Cancellation, true) => "Ce rendez-vous est annul\u00e9.",
            (MailKind.Cancellation, false) => "This event is cancelled.",
            (_, true) => "R\u00e9pondez depuis votre agenda, ou par retour de mail.",
            (_, false) => "Reply from your calendar, or by return mail.",
        });
        return string.Join("\r\n", lines);
    }

    private static string Title(string? summary, string language) =>
        string.IsNullOrWhiteSpace(summary) ? (IsFrench(language) ? "(sans titre)" : "(no title)") : OneLine(summary);

    private static string OneLine(string value) => value.ReplaceLineEndings(" ").Trim();

    private static bool IsFrench(string language) => language.StartsWith("fr", StringComparison.OrdinalIgnoreCase);
    private static CultureInfo Culture(string language) => CultureInfo.GetCultureInfo(IsFrench(language) ? "fr-FR" : "en-GB");
}
