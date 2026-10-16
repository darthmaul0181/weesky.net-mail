using System.Text;
using MimeKit;
using OpenMcdf;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Services;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services;

public sealed class MailMessageExportTests
{
    [Theory]
    [InlineData("Quarterly report", "Quarterly report")]
    [InlineData("Re: a/b\\c*d?e\"f<g>h|i", "Re a b c d e f g h i")]
    [InlineData("  tabs\tand\r\nbreaks  ", "tabs and breaks")]
    [InlineData("Ends with dots...", "Ends with dots")]
    [InlineData("Réunion à 14h — été", "Réunion à 14h — été")]
    [InlineData("", "message")]
    [InlineData(null, "message")]
    [InlineData(" :?* ", "message")]
    [InlineData("Invoice\u202Efdp.exe", "Invoice fdp.exe")]
    [InlineData("zero\u200Bwidth\u2066isolate\u2069", "zero width isolate")]
    public void FileNameOf_KeepsWhatAFileSystemAccepts(string? subject, string expected)
    {
        Assert.Equal(expected, MailMessageExport.FileNameOf(subject));
    }

    [Fact]
    public void FileNameOf_CapsTheLengthWithoutSplittingASurrogatePair()
    {
        var subject = new string('a', MailMessageExport.MaxFileNameLength - 1) + "😀tail";

        var name = MailMessageExport.FileNameOf(subject);

        Assert.Equal(new string('a', MailMessageExport.MaxFileNameLength - 1), name);
    }

    [Fact]
    public void ToMsg_WritesAnOutlookFileCarryingTheSubjectAndTheAttachment()
    {
        var eml = Eml("Quarterly report", withAttachment: true);

        var msg = MailMessageExport.ToMsg(eml);

        Assert.Equal("Quarterly report.msg", msg.FileName);
        Assert.Equal("application/vnd.ms-outlook", msg.ContentType);
        using var root = RootStorage.Open(msg.Content, StorageModeFlags.LeaveOpen);
        Assert.Equal("Quarterly report", ReadUnicode(root, "__substg1.0_0037001F"));
        Assert.Single(root.EnumerateEntries(), e => e.Name.StartsWith("__attach_version1.0_", StringComparison.Ordinal));
    }

    [Fact]
    public void ToMsg_RewindsTheResultSoTheResponseStartsAtTheFirstByte()
    {
        var msg = MailMessageExport.ToMsg(Eml("Hello", withAttachment: false));

        Assert.Equal(0, msg.Content.Position);
        Assert.True(msg.Content.Length > 0);
    }

    private static MailAttachmentContent Eml(string subject, bool withAttachment)
    {
        var body = new BodyBuilder { TextBody = "Numbers inside." };
        if (withAttachment) body.Attachments.Add("figures.csv", Encoding.UTF8.GetBytes("a,b\n1,2\n"));
        var message = new MimeMessage
        {
            Subject = subject,
            Date = new DateTimeOffset(2026, 10, 16, 9, 30, 0, TimeSpan.Zero),
            Body = body.ToMessageBody(),
        };
        message.From.Add(new MailboxAddress("Alice", "alice@weesky.be"));
        message.To.Add(new MailboxAddress("Bob", "bob@example.com"));

        var stream = new MemoryStream();
        message.WriteTo(stream);
        stream.Position = 0;
        return new MailAttachmentContent
        {
            Content = stream,
            FileName = MailMessageExport.FileNameOf(subject) + ".eml",
            ContentType = "message/rfc822",
        };
    }

    private static string ReadUnicode(Storage storage, string name)
    {
        using var stream = storage.OpenStream(name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Encoding.Unicode.GetString(buffer.ToArray()).TrimEnd('\0');
    }
}
