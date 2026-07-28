using System.Text;

namespace weesky.Snoopy.Microservice.Services.Csv;

/// <summary>One record, carrying the file line it started on.</summary>
internal sealed record CsvRecord(int Line, IReadOnlyList<string> Fields);

internal sealed record CsvDocument(IReadOnlyList<string> Header, IReadOnlyList<CsvRecord> Rows);

/// <summary>
/// RFC 4180 with the three things a real file needs beyond it: a byte-order mark, an encoding that
/// may not be UTF-8, and a delimiter that may not be a comma.
/// </summary>
internal static class CsvReader
{
    private static readonly char[] Candidates = [',', ';', '\t'];

    internal static CsvDocument Read(byte[] content)
    {
        var text = Decode(content);
        if (text.Length == 0) return new CsvDocument([], []);

        var records = Parse(text, SniffDelimiter(text));
        return records.Count == 0
            ? new CsvDocument([], [])
            : new CsvDocument(records[0].Fields, [.. records.Skip(1)]);
    }

    /// <summary>
    /// UTF-8 when the bytes decode strictly, Latin-1 otherwise. Latin-1 differs from Windows-1252
    /// only over 0x80–0x9F — typographic quotes and the euro sign, never a letter in a name.
    /// </summary>
    private static string Decode(byte[] content)
    {
        var start = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF
            ? 3 : 0;
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true)
                .GetString(content, start, content.Length - start);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(content, start, content.Length - start);
        }
    }

    // Counted over the header record alone. Read with the wrong delimiter a file does not fail —
    // it comes back as one column, which is an import that silently does nothing.
    private static char SniffDelimiter(string text)
    {
        var best = ',';
        var bestCount = 0;

        foreach (var candidate in Candidates)
        {
            var count = CountInFirstRecord(text, candidate);
            if (count <= bestCount) continue;
            best = candidate;
            bestCount = count;
        }

        return best;
    }

    private static int CountInFirstRecord(string text, char delimiter)
    {
        var count = 0;
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"') continue;
                if (i + 1 < text.Length && text[i + 1] == '"') i++;
                else quoted = false;
            }
            else if (c == '"') quoted = true;
            else if (c == delimiter) count++;
            else if (c is '\r' or '\n') break;
        }

        return count;
    }

    private static List<CsvRecord> Parse(string text, char delimiter)
    {
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var line = 1;
        var recordLine = 1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else { if (c == '\n') line++; field.Append(c); }
                continue;
            }

            switch (c)
            {
                case '"': quoted = true; break;
                case '\r': break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    line++;
                    Flush(records, fields, recordLine);
                    fields.Clear();
                    recordLine = line;
                    break;
                default:
                    if (c == delimiter) { fields.Add(field.ToString()); field.Clear(); }
                    else field.Append(c);
                    break;
            }
        }

        fields.Add(field.ToString());
        Flush(records, fields, recordLine);
        return records;
    }

    // A record of nothing but empty fields is a spreadsheet's trailing line, never a contact.
    private static void Flush(List<CsvRecord> records, List<string> fields, int line)
    {
        if (fields.All(string.IsNullOrWhiteSpace)) return;
        records.Add(new CsvRecord(line, [.. fields]));
    }
}
