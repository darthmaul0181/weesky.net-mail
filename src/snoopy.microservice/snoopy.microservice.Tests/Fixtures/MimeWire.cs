using System.Text;
using MimeKit;

namespace weesky.Snoopy.Microservice.Tests.Fixtures;

/// <summary>A text part as SMTP carries it: MimeKit's <c>Text</c> reads back in the host's line endings,
/// CRLF on Windows and LF on Linux, so an assertion on it passes on one machine and fails on the other.</summary>
internal static class MimeWire
{
    internal static string TextOf(TextPart part, NewLineFormat format = NewLineFormat.Dos)
    {
        var options = FormatOptions.Default.Clone();
        options.NewLineFormat = format;
        using var stream = new MemoryStream();
        part.WriteTo(options, stream, contentOnly: true);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
