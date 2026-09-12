using System.Globalization;
using NodaTime;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

/// <summary>The words of a REPLY, in the two languages the webmail speaks. French carries its
/// non-breaking spaces (U+00A0) before ':' and inside « ». Anything but "fr" is English.</summary>
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
        var title = string.IsNullOrWhiteSpace(summary) ? (IsFrench(language) ? "(sans titre)" : "(no title)") : summary;
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
        var title = string.IsNullOrWhiteSpace(summary) ? (IsFrench(language) ? "(sans titre)" : "(no title)") : summary;
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

    private static bool IsFrench(string language) => language.StartsWith("fr", StringComparison.OrdinalIgnoreCase);
    private static CultureInfo Culture(string language) => CultureInfo.GetCultureInfo(IsFrench(language) ? "fr-FR" : "en-GB");
}
