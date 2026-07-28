using System.Text;

namespace weesky.Snoopy.Microservice.Services.Csv;

internal static class CsvWriter
{
    private const char Delimiter = ',';

    private static readonly char[] MustQuote = [Delimiter, '"', '\r', '\n'];

    /// <summary>
    /// UTF-8 with a byte-order mark: without it Excel reads the file in the system code page and
    /// mangles every accent. <see cref="CsvReader"/> strips it, so a round trip never sees it.
    /// </summary>
    internal static byte[] Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        AppendRecord(builder, header);
        foreach (var row in rows) AppendRecord(builder, row);

        return [.. Encoding.UTF8.GetPreamble(), .. new UTF8Encoding(false).GetBytes(builder.ToString())];
    }

    private static void AppendRecord(StringBuilder builder, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0) builder.Append(Delimiter);
            builder.Append(Quote(fields[i]));
        }

        builder.Append("\r\n");
    }

    // Quoted only where it has to be: a wholly quoted file is valid and unreadable at a glance.
    private static string Quote(string field) =>
        field.IndexOfAny(MustQuote) >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
}
