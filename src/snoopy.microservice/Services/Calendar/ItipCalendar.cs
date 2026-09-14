using System.Globalization;
using System.Text;
using weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

namespace weesky.Snoopy.Microservice.Services.Calendar;

/// <summary>
/// The reduced VCALENDAR an iTIP REPLY or CANCEL carries (RFC 5546), and the content-line rules
/// every line the server writes itself obeys: names through <see cref="IcsComposer.CommonName"/>,
/// TEXT escaped, lines folded at 75 octets.
/// </summary>
internal static class ItipCalendar
{
    internal const string ProductId = "-//weesky//webmail//EN";

    private const int FoldAt = 75;

    /// <summary>METHOD, UID, SEQUENCE, DTSTAMP, ORGANIZER, one ATTENDEE per entry (PARTSTAT when given),
    /// the file's own DTSTART line, SUMMARY when there is one, then STATUS when given. Refused when a
    /// value it writes as it reads would not stay on its own line: see <see cref="CarriesUid"/>.</summary>
    internal static string Reduced(
        string method, string uid, int sequence, DateTime nowUtc, string organizerEmail, string? organizerName,
        IEnumerable<(string Email, string? Name, string? PartStat)> attendees, string? dtStartLine, string? summary,
        string? status = null)
    {
        var guests = attendees.ToList();
        if (InvitationMailer.Mailbox(organizerEmail) is null || guests.Any(a => InvitationMailer.Mailbox(a.Email) is null))
            throw new ArgumentException("An iTIP line may only carry a deliverable address");
        if (!CarriesUid(uid)) throw new ArgumentException("A UID holding a control character cannot be written on one line", nameof(uid));

        List<string> lines =
        [
            "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:" + ProductId, "METHOD:" + method, "BEGIN:VEVENT",
            "UID:" + uid,
            "SEQUENCE:" + sequence.ToString(CultureInfo.InvariantCulture),
            "DTSTAMP:" + nowUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture),
            "ORGANIZER" + Cn(organizerName) + ":mailto:" + organizerEmail,
        ];
        lines.AddRange(guests.Select(a =>
            "ATTENDEE" + (a.PartStat is { } partStat ? ";PARTSTAT=" + partStat : string.Empty) + Cn(a.Name) + ":mailto:" + a.Email));
        if (dtStartLine is not null) lines.Add(dtStartLine);
        if (!string.IsNullOrWhiteSpace(summary)) lines.Add("SUMMARY:" + EscapeText(summary));
        if (status is not null) lines.Add("STATUS:" + status);
        lines.AddRange(["END:VEVENT", "END:VCALENDAR"]);

        var ics = new StringBuilder();
        foreach (var physical in lines.SelectMany(Fold)) ics.Append(physical).Append("\r\n");
        return ics.ToString();
    }

    /// <summary>Whether the UID is written as it reads without splicing a line: Ical.Net unescapes a "\n".</summary>
    internal static bool CarriesUid(string uid) => !uid.Any(char.IsControl);

    /// <summary>The ";CN=…" parameter, or nothing. Cleaned by <see cref="IcsComposer.CommonName"/> (no line
    /// break can splice a line in), then RFC 5545 § 3.2: a value holding ':' ';' or ',' is quoted.</summary>
    internal static string Cn(string? name) => IcsComposer.CommonName(name) is { } clean
        ? ";CN=" + (clean.IndexOfAny([':', ';', ',']) >= 0 ? $"\"{clean}\"" : clean)
        : string.Empty;

    /// <summary>RFC 5545 § 3.3.11.</summary>
    internal static string EscapeText(string value) =>
        value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");

    /// <summary>RFC 5545 § 3.1: physical lines of at most 75 octets, continuations led by a space.
    /// Counted in UTF-8 octets, never inside a multi-byte sequence.</summary>
    internal static IEnumerable<string> Fold(string logical)
    {
        if (Encoding.UTF8.GetByteCount(logical) <= FoldAt) { yield return logical; yield break; }

        var bytes = Encoding.UTF8.GetBytes(logical);
        var at = 0;
        var limit = FoldAt;
        while (at < bytes.Length)
        {
            var take = Math.Min(limit, bytes.Length - at);
            while (take > 0 && at + take < bytes.Length && (bytes[at + take] & 0xC0) == 0x80) take--;
            yield return (at == 0 ? "" : " ") + Encoding.UTF8.GetString(bytes, at, take);
            at += take;
            limit = FoldAt - 1;
        }
    }
}
