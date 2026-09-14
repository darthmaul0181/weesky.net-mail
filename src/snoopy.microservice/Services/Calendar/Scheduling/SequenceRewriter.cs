using weesky.Snoopy.Microservice.Services.Calendar.Invitations;

namespace weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

/// <summary>SEQUENCE advanced on the components named, textually: the file a device just wrote
/// stays its own file but for that number (décision 9). Each component carries its own version,
/// so an override moved alone advances alone, as <c>IcsComposer.RewriteOne</c> does.</summary>
internal static class SequenceRewriter
{
    /// <summary>Each named component gets one more than the greater of its own SEQUENCE and its
    /// reference, so a copy whose number went down still ends above what the invitees hold.</summary>
    internal static string Bump(string ics, IReadOnlyDictionary<string, int> references)
    {
        if (references.Count == 0) return ics;
        var newline = PartStatRewriter.NewlineOf(ics);
        var physical = ics.Split(newline);
        var lines = PartStatRewriter.Unfold(physical);
        var output = new List<string>();
        var i = 0;
        while (i < lines.Count)
        {
            if (!lines[i].Text.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            { output.AddRange(Physical(physical, lines[i])); i++; continue; }
            var end = lines.FindIndex(i, l => l.Text.StartsWith("END:VEVENT", StringComparison.OrdinalIgnoreCase));
            if (end < 0) end = lines.Count - 1;
            var component = lines.GetRange(i, end - i + 1);
            output.AddRange(references.TryGetValue(KeyOf(component), out var reference)
                ? Bumped(component, physical, reference) : Original(component, physical));
            i = end + 1;
        }
        return string.Join(newline, output);
    }

    private static string KeyOf(List<PartStatRewriter.Line> component)
    {
        var index = IndexOfOwnProperty(component, "RECURRENCE-ID");
        if (index < 0) return "";
        var text = component[index].Text;
        var valueStart = PartStatRewriter.ValueStart(text);
        return valueStart < 0 ? "" : text[(valueStart + 1)..];
    }

    /// <summary>Long, not int, and the parameters stay: a SEQUENCE past 2^31 is still a number to
    /// advance, and "SEQUENCE;X-FOO=BAR:2" keeps its parameter, only the value moving. A value
    /// that is not a number at all leaves the component untouched rather than guessed at.</summary>
    private static IEnumerable<string> Bumped(List<PartStatRewriter.Line> component, string[] physical, int reference)
    {
        var sequenceIndex = IndexOfOwnProperty(component, "SEQUENCE");
        string line;
        if (sequenceIndex < 0) line = "SEQUENCE:" + (Math.Max(0, reference) + 1);
        else
        {
            var text = component[sequenceIndex].Text;
            var valueStart = PartStatRewriter.ValueStart(text);
            if (valueStart < 0 || !long.TryParse(text[(valueStart + 1)..], out var current)) return Original(component, physical);
            line = text[..(valueStart + 1)] + (Math.Max(current, reference) + 1);
        }

        var uidIndex = sequenceIndex < 0 ? IndexOfOwnProperty(component, "UID") : -1;
        return Rewritten(component, physical, sequenceIndex, uidIndex, line);
    }

    private static IEnumerable<string> Rewritten(List<PartStatRewriter.Line> component, string[] physical, int sequenceIndex, int uidIndex, string line)
    {
        for (var j = 0; j < component.Count; j++)
        {
            if (j == sequenceIndex) { yield return line; continue; }
            foreach (var p in Physical(physical, component[j])) yield return p;
            if (j == uidIndex) yield return line;
        }
    }

    /// <summary>The index of a property directly in the VEVENT — never one a nested block (a
    /// VALARM, RFC 9074) carries of its own — or -1. <paramref name="component"/> runs from its
    /// own BEGIN:VEVENT to its own END:VEVENT, excluded from the depth count.</summary>
    private static int IndexOfOwnProperty(List<PartStatRewriter.Line> component, string property)
    {
        var depth = 0;
        for (var j = 0; j < component.Count; j++)
        {
            var isBoundary = j == 0 || j == component.Count - 1;
            var text = component[j].Text;
            if (depth == 0 && !isBoundary && PartStatRewriter.IsProperty(text, property)) return j;
            if (isBoundary) continue;
            if (text.StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase)) depth++;
            else if (text.StartsWith("END:", StringComparison.OrdinalIgnoreCase)) depth--;
        }
        return -1;
    }

    private static IEnumerable<string> Original(List<PartStatRewriter.Line> component, string[] physical) =>
        component.SelectMany(l => Physical(physical, l));

    private static IEnumerable<string> Physical(string[] physical, PartStatRewriter.Line line) =>
        physical.Skip(line.First).Take(line.Count);
}
