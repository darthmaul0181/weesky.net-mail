using System.Text;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// How an uploaded text file is turned into a string. Not specific to one format: a CSV and a
/// vCard come off the same pickers, from the same address books, in the same encodings.
/// </summary>
internal static class FileText
{
    /// <summary>
    /// UTF-8 when the bytes decode strictly, Latin-1 otherwise, byte-order mark dropped. Latin-1
    /// differs from Windows-1252 only over 0x80–0x9F — typographic quotes and the euro sign, never
    /// a letter in a name. Decoding with the replacement fallback instead would turn every accent
    /// of an Outlook or phone export into U+FFFD, and a card stored verbatim keeps that forever.
    /// </summary>
    internal static string Decode(byte[] content)
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
}
