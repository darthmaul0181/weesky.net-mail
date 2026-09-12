using System.Text;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

/// <summary>
/// Textual edits of an iCalendar file, so what the organizer sent enters the calendar byte for byte
/// but for the one line the user answers on (décision 4). Ical.Net would re-serialize the whole
/// file and change every byte of it; this works on logical lines (RFC 5545 § 3.1) and refolds
/// only the line it rewrote.
/// </summary>
internal static class PartStatRewriter
{
    private const int FoldAt = 75;

    /// <summary>One logical line and the physical lines it was folded over.</summary>
    internal readonly record struct Line(string Text, int First, int Count);

    /// <summary>
    /// Rewrites <b>every</b> ATTENDEE whose value is <c>mailto:address</c> (case-insensitive) with
    /// <paramref name="partStat"/> and no RSVP, drops METHOD, leaves every other byte. Every
    /// component is answered, not only the master: RFC 5546 carries one PARTSTAT per component, so
    /// an answer missing from an override would leave the moved dates unanswered. Null when no
    /// ATTENDEE names the address, and null when <paramref name="partStat"/> is not a partstat
    /// token — a value carrying a newline would splice lines of its own into the stored file.
    /// </summary>
    internal static string? Rewrite(string ics, string address, string partStat)
    {
        if (!IsPartStatToken(partStat)) return null;

        var newline = NewlineOf(ics);
        var physical = ics.Split(newline);
        var lines = Unfold(physical);
        var targets = lines.Where(l => IsAttendeeOf(l.Text, address)).Select(l => l.First).ToHashSet();
        if (targets.Count == 0) return null;

        var output = new List<string>();
        foreach (var line in lines)
        {
            if (line.Text.StartsWith("METHOD:", StringComparison.OrdinalIgnoreCase)) continue;
            if (targets.Contains(line.First)) output.AddRange(Fold(WithPartStat(line.Text, partStat)));
            else output.AddRange(physical.Skip(line.First).Take(line.Count));
        }

        return string.Join(newline, output);
    }

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

    private static string NewlineOf(string ics) => ics.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static bool IsProperty(string line, string property) =>
        line.StartsWith(property + ":", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith(property + ";", StringComparison.OrdinalIgnoreCase);

    private static List<Line> Unfold(string[] physical)
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
        if (value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) value = value["mailto:".Length..];
        return string.Equals(value, address, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The parameters minus PARTSTAT and RSVP, then PARTSTAT last, then the value.</summary>
    private static string WithPartStat(string line, string partStat)
    {
        var colon = ValueStart(line);
        var head = line[..colon];
        var value = line[colon..];
        var parameters = SplitParameters(head).Skip(1)
            .Where(p => !p.StartsWith("PARTSTAT=", StringComparison.OrdinalIgnoreCase)
                && !p.StartsWith("RSVP=", StringComparison.OrdinalIgnoreCase));
        return "ATTENDEE;" + string.Join(';', parameters.Append("PARTSTAT=" + partStat)) + value;
    }

    /// <summary>The ':' that ends the name-and-parameters, ignoring any inside a quoted parameter.</summary>
    private static int ValueStart(string line)
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

    /// <summary>RFC 5545 § 3.1: physical lines of at most 75 octets, continuations led by a space.
    /// Counted in UTF-8 octets, never inside a multi-byte sequence.</summary>
    private static IEnumerable<string> Fold(string logical)
    {
        var bytes = Encoding.UTF8.GetBytes(logical);
        if (bytes.Length <= FoldAt) { yield return logical; yield break; }

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
