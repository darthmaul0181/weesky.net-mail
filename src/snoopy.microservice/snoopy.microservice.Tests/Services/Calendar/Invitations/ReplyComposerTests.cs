using System.Globalization;
using MimeKit;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;

public sealed class ReplyComposerTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 8, 0, 0, DateTimeKind.Utc);

    private static ReplyInput Input(string partStat = "ACCEPTED", string language = "fr") => new(
        "alice@weesky.be", "Alice Martin", "marc.dupont@example.org", "Marc Dupont",
        InvitationParser.Read(InvitationParserTests.Fixture("google-request")).Invitation!,
        partStat, "Europe/Brussels", language, Now);

    [Fact]
    public void Envelope_FromTheUserToTheOrganizer_LocalisedSubject()
    {
        var message = ReplyComposer.Compose(Input());

        Assert.Equal("alice@weesky.be", message.From.Mailboxes.Single().Address);
        Assert.Equal("Alice Martin", message.From.Mailboxes.Single().Name);
        Assert.Equal("marc.dupont@example.org", message.To.Mailboxes.Single().Address);
        Assert.Equal("Accept\u00e9\u00A0: D\u00eener chez Marc", message.Subject);
        Assert.Equal(Now, message.Date.UtcDateTime);
    }

    [Fact]
    public void Body_IsAlternative_TextThenCalendarWithMethodReply()
    {
        var message = ReplyComposer.Compose(Input("DECLINED", "en"));

        var alternative = Assert.IsType<MultipartAlternative>(message.Body);
        var text = Assert.IsType<TextPart>(alternative[0]);
        Assert.Equal("Alice Martin has declined the invitation \u201CD\u00eener chez Marc\u201D on Saturday 10 October 2026, 19:30.", text.Text.TrimEnd());
        var calendar = Assert.IsType<TextPart>(alternative[1]);
        Assert.True(calendar.ContentType.IsMimeType("text", "calendar"));
        Assert.Equal("REPLY", calendar.ContentType.Parameters["method"]);
        Assert.Equal("utf-8", calendar.ContentType.Charset);
    }

    [Fact]
    public void Calendar_IsTheReducedReply_ThunderbirdsShape()
    {
        var ics = ReplyComposer.Calendar(Input("TENTATIVE"));

        Assert.Equal(
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//weesky//webmail//EN\r\nMETHOD:REPLY\r\n"
            + "BEGIN:VEVENT\r\nUID:7c2e1c4a9f0b4d2e8a1c3b5d7e9f1a2b@google.com\r\nSEQUENCE:0\r\nDTSTAMP:20260913T080000Z\r\n"
            + "ORGANIZER;CN=Marc Dupont:mailto:marc.dupont@example.org\r\n"
            + "ATTENDEE;PARTSTAT=TENTATIVE;CN=Alice Martin:mailto:alice@weesky.be\r\n"
            + "DTSTART;TZID=Europe/Brussels:20261010T193000\r\n"
            + "SUMMARY:D\u00eener chez Marc\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n", ics);
    }

    [Fact]
    public void Calendar_QuotesACommonNameHoldingAColonOrComma_AndEscapesTheSummary()
    {
        var input = Input() with { FromName = "Martin, Alice", Invitation = Input().Invitation with { Summary = "D\u00eener; chez Marc, 19h" } };

        var ics = ReplyComposer.Calendar(input);

        Assert.Contains("ATTENDEE;PARTSTAT=ACCEPTED;CN=\"Martin, Alice\":mailto:alice@weesky.be\r\n", ics);
        Assert.Contains("SUMMARY:D\u00eener\\; chez Marc\\, 19h\r\n", ics);
    }

    [Fact]
    public void Calendar_WithoutNames_OmitsCn()
    {
        var ics = ReplyComposer.Calendar(Input() with { FromName = null, OrganizerName = null });

        Assert.Contains("ORGANIZER:mailto:marc.dupont@example.org\r\n", ics);
        Assert.Contains("ATTENDEE;PARTSTAT=ACCEPTED:mailto:alice@weesky.be\r\n", ics);
    }

    [Fact]
    public void Calendar_StampsInInvariantCulture_WhateverTheThreadCarries()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // Umm al-Qura: "yyyy" answers 1448 under it, so a stamp read in the thread's own
            // culture is not the date the organizer's agenda would pair the REPLY on.
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            Assert.Contains("DTSTAMP:20260913T080000Z", ReplyComposer.Calendar(Input()));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Calendar_NeverLetsADisplayNameSpliceALine()
    {
        var ics = ReplyComposer.Calendar(
            Input() with { FromName = "Alice\r\nATTENDEE;PARTSTAT=ACCEPTED:mailto:mallory@example.org" });

        Assert.Single(ics.Split("\r\n"), line => line.StartsWith("ATTENDEE"));
        Assert.DoesNotContain("mailto:mallory@example.org\r\n", ics);
    }
}
