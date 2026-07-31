using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Reads the priority a sender declared. Four headers are consulted in the order clients actually
/// write them, and a header that is present but unreadable falls through to the next — but a
/// header that is readable ends the search, so an explicit "3" is an explicit Normal rather than
/// an invitation to consult Importance behind the sender's back.
/// </summary>
internal static class MailPriorityReader
{
    /// <summary>The header fields a summary FETCH has to ask for. Order matches the search below.</summary>
    public static readonly string[] Fields = ["X-Priority", "Importance", "X-MSMail-Priority", "Priority"];

    public static MailPriority Parse(HeaderList headers) =>
        FromXPriority(headers)
        ?? FromWord(headers, "Importance", "high", "normal", "low")
        ?? FromWord(headers, "X-MSMail-Priority", "high", "normal", "low")
        ?? FromWord(headers, "Priority", "urgent", "normal", "non-urgent")
        ?? MailPriority.Normal;

    // "1 (Highest)" — the digits are the value, the parenthesised comment is decoration.
    private static MailPriority? FromXPriority(HeaderList headers)
    {
        var header = headers.Topmost("X-Priority");
        if (header is null) return null;

        var text = header.Value.TrimStart();
        var end = 0;
        while (end < text.Length && char.IsAsciiDigit(text[end])) end++;
        if (end == 0 || !int.TryParse(text[..end], out var level)) return null;

        return level switch
        {
            1 or 2 => MailPriority.High,
            3 => MailPriority.Normal,
            4 or 5 => MailPriority.Low,
            _ => (MailPriority?)null
        };
    }

    private static MailPriority? FromWord(HeaderList headers, string field, string high, string normal, string low)
    {
        var value = headers.Topmost(field)?.Value.Trim();
        if (value is null) return null;

        if (string.Equals(value, high, StringComparison.OrdinalIgnoreCase)) return MailPriority.High;
        if (string.Equals(value, normal, StringComparison.OrdinalIgnoreCase)) return MailPriority.Normal;
        if (string.Equals(value, low, StringComparison.OrdinalIgnoreCase)) return MailPriority.Low;
        return null;
    }
}
