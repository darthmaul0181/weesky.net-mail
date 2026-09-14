using System.Text;
using System.Text.RegularExpressions;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

/// <summary>
/// Textual edits of an iCalendar file, so what the organizer sent enters the calendar byte for byte
/// but for the one line the user answers on (décision 4). Ical.Net would re-serialize the whole
/// file and change every byte of it; this works on logical lines (RFC 5545 § 3.1) and refolds
/// only the line it rewrote.
/// </summary>
internal static partial class PartStatRewriter
{
    /// <summary>The DTSTAMP of the REPLY that wrote a guest's PARTSTAT, on that ATTENDEE line (décision 12).</summary>
    internal const string ReplyStampParameter = "X-WEESKY-REPLY-STAMP";

    private const string Dropped = "";

    /// <summary>One logical line and the physical lines it was folded over.</summary>
    internal readonly record struct Line(string Text, int First, int Count);

    /// <summary>
    /// Rewrites <b>every</b> ATTENDEE whose value is <c>mailto:address</c> (case-insensitive) with
    /// <paramref name="partStat"/> and no RSVP, drops METHOD, leaves every other byte. Every
    /// component is answered, not only the master: RFC 5546 carries one PARTSTAT per component, so
    /// an answer missing from an override would leave the moved dates unanswered. Null when no
    /// ATTENDEE names the address, and null when <paramref name="partStat"/> is not a partstat
    /// token — a value carrying a newline would splice lines of its own into the stored file.
    /// A <paramref name="stamp"/> — the DTSTAMP of the guest's REPLY — replaces the line's
    /// <see cref="ReplyStampParameter"/> right after the PARTSTAT; null when it is not a UTC basic date-time.
    /// </summary>
    internal static string? Rewrite(string ics, string address, string partStat, string? stamp = null)
    {
        if (!IsPartStatToken(partStat) || stamp is not null && !IsReplyStamp(stamp)) return null;

        var answered = false;
        var rewritten = Refold(ics, text =>
        {
            if (text.StartsWith("METHOD:", StringComparison.OrdinalIgnoreCase)) return Dropped;
            if (!IsAttendeeOf(text, address)) return null;
            answered = true;
            return WithPartStat(text, partStat, stamp);
        });
        return answered ? rewritten : null;
    }

    /// <summary>The file minus <see cref="ReplyStampParameter"/> on every ATTENDEE line, what a mail
    /// carries of the stored file (décision 12): that bookkeeping is ours. Every other byte stays.</summary>
    internal static string WithoutReplyStamp(string ics) => Refold(ics, text =>
    {
        if (!IsProperty(text, "ATTENDEE") || ValueStart(text) is var colon && colon < 0) return null;
        var parts = SplitParameters(text[..colon]).ToList();
        return parts.Any(IsReplyStampParameter)
            ? string.Join(';', parts.Where(p => !IsReplyStampParameter(p))) + text[colon..]
            : null;
    });

    /// <summary>One pass over the logical lines: <paramref name="rewrite"/> answers null to keep a line's
    /// physical bytes, <see cref="Dropped"/> to leave it out, or its new text, which is refolded.</summary>
    private static string Refold(string ics, Func<string, string?> rewrite)
    {
        var newline = NewlineOf(ics);
        var physical = ics.Split(newline);
        var output = new List<string>();
        foreach (var line in Unfold(physical))
        {
            var text = rewrite(line.Text);
            if (text is null) output.AddRange(physical.Skip(line.First).Take(line.Count));
            else if (text.Length > 0) output.AddRange(ItipCalendar.Fold(text));
        }

        return string.Join(newline, output);
    }

    private static bool IsReplyStampParameter(string parameter) =>
        parameter.StartsWith(ReplyStampParameter + "=", StringComparison.OrdinalIgnoreCase);

    /// <summary>RFC 5545 § 3.3.5, form #2 in basic format: fixed width, so two stamps order as strings.</summary>
    internal static bool IsReplyStamp(string value) => ReplyStamp().IsMatch(value);

    [GeneratedRegex(@"\A[0-9]{8}T[0-9]{6}Z\z", RegexOptions.CultureInvariant)]
    private static partial Regex ReplyStamp();

    /// <summary>RFC 5545 § 3.2.12: a partstat is an iana-token or an x-name, both of which are
    /// letters, digits and '-'. Nothing else may reach a line of the stored file.</summary>
    private static bool IsPartStatToken(string partStat) =>
        partStat.Length > 0 && partStat.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    /// <summary>Drops the METHOD line and nothing else.</summary>
    internal static string StripMethod(string ics)
    {
        var newline = NewlineOf(ics);
        return string.Join(newline, ics.Split(newline)
            .Where(l => !l.StartsWith("METHOD:", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The first logical line of a property, unfolded — "DTSTART;TZID=…:…" — read inside the one
    /// component the reader describes: the VEVENT carrying no RECURRENCE-ID, or the first one when
    /// none does, which is <see cref="IcsDocument.MasterOf"/>'s rule spelled on the text. File
    /// order alone would answer a VTIMEZONE's own DTSTART, a VALARM's DESCRIPTION, or an override
    /// written before its master. Null when the component carries no such property.
    /// </summary>
    internal static string? LineOf(string ics, string property)
    {
        var components = ComponentsOf(Unfold(ics));
        var described = components.FirstOrDefault(c => !c.Any(l => IsProperty(l, "RECURRENCE-ID")))
            ?? components.FirstOrDefault();
        return described?.FirstOrDefault(l => IsProperty(l, property));
    }

    /// <summary>The logical lines each VEVENT owns, the blocks nested in it — a VALARM — left out.</summary>
    private static List<List<string>> ComponentsOf(List<Line> lines)
    {
        var components = new List<List<string>>();
        List<string>? current = null;
        var nested = 0;
        foreach (var text in lines.Select(l => l.Text))
        {
            if (text.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = [];
                nested = 0;
                components.Add(current);
            }
            else if (text.StartsWith("END:VEVENT", StringComparison.OrdinalIgnoreCase)) current = null;
            else if (current is null) continue;
            else if (text.StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase)) nested++;
            else if (text.StartsWith("END:", StringComparison.OrdinalIgnoreCase)) nested--;
            else if (nested == 0) current.Add(text);
        }

        return components;
    }

    internal static List<Line> Unfold(string ics) => Unfold(ics.Split(NewlineOf(ics)));

    internal static string NewlineOf(string ics) => ics.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    internal static bool IsProperty(string line, string property) =>
        line.StartsWith(property + ":", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith(property + ";", StringComparison.OrdinalIgnoreCase);

    internal static List<Line> Unfold(string[] physical)
    {
        var lines = new List<Line>();
        var text = new StringBuilder();
        var first = 0;
        for (var i = 0; i < physical.Length; i++)
        {
            var continues = physical[i].Length > 0 && (physical[i][0] == ' ' || physical[i][0] == '\t');
            if (continues && text.Length > 0) { text.Append(physical[i], 1, physical[i].Length - 1); continue; }
            if (i > first || text.Length > 0) lines.Add(new Line(text.ToString(), first, i - first));
            text.Clear().Append(physical[i]);
            first = i;
        }

        lines.Add(new Line(text.ToString(), first, physical.Length - first));
        return lines;
    }

    private static bool IsAttendeeOf(string line, string address)
    {
        if (!line.StartsWith("ATTENDEE", StringComparison.OrdinalIgnoreCase)) return false;
        var colon = ValueStart(line);
        if (colon < 0) return false;
        var value = line[(colon + 1)..].Trim();
        if (value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) value = IcsProjector.Decoded(value["mailto:".Length..]);
        return string.Equals(value, address, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The parameters minus PARTSTAT and RSVP, then PARTSTAT, then the stamp when one is given
    /// (replacing the line's own), then the value.</summary>
    private static string WithPartStat(string line, string partStat, string? stamp)
    {
        var colon = ValueStart(line);
        var head = line[..colon];
        var value = line[colon..];
        var parameters = SplitParameters(head).Skip(1)
            .Where(p => !p.StartsWith("PARTSTAT=", StringComparison.OrdinalIgnoreCase)
                && !p.StartsWith("RSVP=", StringComparison.OrdinalIgnoreCase)
                && (stamp is null || !IsReplyStampParameter(p)))
            .Append("PARTSTAT=" + partStat);
        if (stamp is not null) parameters = parameters.Append(ReplyStampParameter + "=" + stamp);
        return "ATTENDEE;" + string.Join(';', parameters) + value;
    }

    /// <summary>The ':' that ends the name-and-parameters, ignoring any inside a quoted parameter.</summary>
    internal static int ValueStart(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ':' && !quoted) return i;
        }

        return -1;
    }

    private static IEnumerable<string> SplitParameters(string head)
    {
        var quoted = false;
        var start = 0;
        for (var i = 0; i < head.Length; i++)
        {
            if (head[i] == '"') quoted = !quoted;
            else if (head[i] == ';' && !quoted) { yield return head[start..i]; start = i + 1; }
        }

        yield return head[start..];
    }
}
