using System.Text;
using MailKit;
using MimeKit;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Services;
using weesky.Scotty.Microservice.Services.Calendar;
using weesky.Scotty.Microservice.Services.Calendar.Delivery;
using weesky.Scotty.Microservice.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services.Calendar.Delivery;

public sealed class DeliveryMailReaderTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    internal static Stream Mail(string name) =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Mails", name + ".eml"));

    [Theory]
    [InlineData("outlook-reply", "ACCEPTED")]
    [InlineData("gmail-reply", "DECLINED")]
    [InlineData("ics-attachment-only", "TENTATIVE")]
    public async Task Read_FindsTheReply_TheSamePartTheImapRuleWouldPick(string mail, string partStat)
    {
        using var stream = Mail(mail);
        var read = await DeliveryMailReader.ReadAsync(stream, None);

        Assert.Equal(DeliveryPartStatus.Found, read.Status);
        var parsed = InvitationParser.Read(read.Ics!).Invitation!;
        Assert.Equal((InvitationMethod.Reply, "aaaa1111-bbbb-2222-cccc-3333dddd4444", partStat),
            (parsed.Method, parsed.Uid, parsed.Attendees[0].PartStat));
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("forwarded-reply")]
    public async Task Read_FindsNothing_WhenNoPartIsRetained(string mail)
    {
        using var stream = Mail(mail);
        Assert.Equal(new DeliveryCalendarPart(DeliveryPartStatus.None, null), await DeliveryMailReader.ReadAsync(stream, None));
    }

    /// <summary>The two doors read the same parts: MimeKit's BodyParts stops at message/rfc822, as
    /// MailKit's does — the forwarded reply above is reached by neither. A BodyPartMessage is not a
    /// calendar part on the IMAP side either.</summary>
    [Fact]
    public void TheImapRule_DoesNotSeeInsideAForwardedMessage()
    {
        var forwarded = new BodyPartMessage { ContentType = new ContentType("message", "rfc822") };
        Assert.Null(MailMessageMapper.CalendarPart([new BodyPartText { ContentType = new ContentType("text", "plain") }, forwarded]));
    }

    [Fact]
    public async Task Read_SaysTooLarge_PastTheIcsCeiling()
    {
        var big = "From: a@b\r\nContent-Type: text/calendar; method=REPLY\r\n\r\n" + new string('x', IcsGuards.MaxIcsBytes + 1);
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(big));
        Assert.Equal(DeliveryPartStatus.TooLarge, (await DeliveryMailReader.ReadAsync(stream, None)).Status);
    }

    [Fact]
    public async Task Read_SurvivesDeepNesting_AndFindsNothing()
    {
        var builder = new StringBuilder("From: a@b\r\nMIME-Version: 1.0\r\n");
        for (var depth = 0; depth < 200; depth++)
            builder.Append($"Content-Type: multipart/mixed; boundary=\"b{depth}\"\r\n\r\n--b{depth}\r\n");
        builder.Append("Content-Type: text/calendar; method=REPLY\r\n\r\nBEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n");
        for (var depth = 199; depth >= 0; depth--) builder.Append($"--b{depth}--\r\n");
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(builder.ToString()));

        var read = await DeliveryMailReader.ReadAsync(stream, None);

        Assert.Equal(DeliveryPartStatus.None, read.Status);
    }

    [Fact]
    public async Task Read_RefusesMoreThanMaxParts()
    {
        var builder = new StringBuilder("From: a@b\r\nMIME-Version: 1.0\r\nContent-Type: multipart/mixed; boundary=\"b\"\r\n\r\n");
        for (var i = 0; i <= DeliveryMailReader.MaxParts; i++) builder.Append("--b\r\nContent-Type: text/plain\r\n\r\nx\r\n");
        builder.Append("--b\r\nContent-Type: text/calendar; method=REPLY\r\n\r\nBEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n--b--\r\n");
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(builder.ToString()));

        Assert.Equal(DeliveryPartStatus.None, (await DeliveryMailReader.ReadAsync(stream, None)).Status);
    }

    [Fact]
    public async Task Read_GarbageIsNothing()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("not a mail at all"));
        Assert.Equal(DeliveryPartStatus.None, (await DeliveryMailReader.ReadAsync(stream, None)).Status);
    }
}
