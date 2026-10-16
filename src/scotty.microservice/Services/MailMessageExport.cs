using System.Buffers;
using System.Globalization;
using MimeKit.IO;
using weesky.Scotty.Microservice.Models.Mail;

namespace weesky.Scotty.Microservice.Services;

/// <summary>
/// A message saved as a file: its name, built from the subject, and its Outlook (.msg) form.
/// </summary>
internal static class MailMessageExport
{
    public const int MaxFileNameLength = 100;
    public const string MsgContentType = "application/vnd.ms-outlook";

    // Windows' set, whatever the host: the file lands on the reader's disk, not the server's.
    private static readonly SearchValues<char> Forbidden = SearchValues.Create("\\/:*?\"<>|");

    public static string FileNameOf(string? subject)
    {
        var words = (subject ?? string.Empty)
            .Select(c => char.IsControl(c) || char.IsWhiteSpace(c) || Forbidden.Contains(c) || IsFormat(c) ? ' ' : c)
            .ToArray();
        var name = string.Join(' ', new string(words).Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (name.Length > MaxFileNameLength)
        {
            var cut = char.IsHighSurrogate(name[MaxFileNameLength - 1]) ? MaxFileNameLength - 1 : MaxFileNameLength;
            name = name[..cut];
        }

        name = name.TrimEnd('.', ' ');
        return name.Length == 0 ? "message" : name;
    }

    // A bidi override or a zero-width character lets a sender make "x\u202Efdp.exe" read as "xexe.pdf".
    private static bool IsFormat(char c) => char.GetUnicodeCategory(c) == UnicodeCategory.Format;

    /// <summary>Converts the raw message; the caller still owns, and disposes, both streams.</summary>
    public static MailAttachmentContent ToMsg(MailAttachmentContent eml)
    {
        var msg = new MemoryBlockStream();
        try
        {
            MsgKit.Converter.ConvertEmlToMsg(eml.Content, msg);
        }
        catch
        {
            msg.Dispose();
            throw;
        }
        msg.Position = 0;

        return new MailAttachmentContent
        {
            Content = msg,
            FileName = Path.ChangeExtension(eml.FileName, ".msg"),
            ContentType = MsgContentType,
        };
    }
}
