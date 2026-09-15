using System.Globalization;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>Text from a mail, made safe for the plain-text log: the file sink escapes nothing, so a
/// CR/LF in a UID would forge a log line. One replacement per control character, a bounded length.
/// U+2028/U+2029 are replaced alongside the C0/C1 controls <see cref="char.IsControl(char)"/>
/// already catches: neither is a control character by the Unicode category, but both render as a
/// line break in a browser reading the log, the same forged-line risk CR/LF exists to close.</summary>
internal static class LogText
{
    internal static string Safe(string? value, int max = 200)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var cut = value.Length > max ? value[..max] + "…" : value;
        return string.Create(cut.Length, cut, static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = char.IsControl(source[i])
                    || char.GetUnicodeCategory(source[i]) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                    ? '?' : source[i];
        });
    }
}
