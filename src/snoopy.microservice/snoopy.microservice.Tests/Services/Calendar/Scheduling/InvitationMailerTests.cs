using System.Text;
using MimeKit;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using weesky.Snoopy.Microservice.Services.Calendar.Scheduling;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Scheduling;

public class InvitationMailerTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc);
    private static readonly OrganizerWrite Alice = new("alice@weesky.be", "Alice");
    private static string Stored => InvitationParserTests.Fixture("webmail-invited");

    private static InvitationMailInput Input(MailKind kind, params string[] to) =>
        new(kind, Stored, CancelSequence: 1, to, Alice, "fr", Now);

    private static MultipartAlternative Alternative(MimeMessage message) =>
        Assert.IsType<MultipartAlternative>(Assert.IsType<Multipart>(message.Body)[0]);

    private static string Attached(MimeMessage message)
    {
        var attachment = Assert.IsType<MimePart>(Assert.IsType<Multipart>(message.Body)[1]);
        Assert.NotNull(attachment.Content);
        using var bytes = new MemoryStream();
        attachment.Content.DecodeTo(bytes);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    [Fact]
    public void Request_CarriesTheStoredFileWithMethod_TwiceOver_FromTheOrganizer()
    {
        var message = InvitationMailer.Compose(Input(MailKind.Invitation, "marc.dupont@example.org", "julie@example.net"));

        Assert.Equal("Alice", message.From.Mailboxes.Single().Name);
        Assert.Equal("alice@weesky.be", message.From.Mailboxes.Single().Address);
        Assert.Equal(["marc.dupont@example.org", "julie@example.net"], message.To.Mailboxes.Select(m => m.Address));
        Assert.Equal("Invitation\u00A0: R\u00e9union de rentr\u00e9e", message.Subject);
        Assert.Equal(new DateTimeOffset(Now), message.Date);

        var mixed = Assert.IsType<Multipart>(message.Body);
        Assert.Equal("multipart/mixed", mixed.ContentType.MimeType);
        var alternative = Assert.IsType<MultipartAlternative>(mixed[0]);
        var text = Assert.IsType<TextPart>(alternative[0]);
        Assert.True(text.IsPlain);
        Assert.StartsWith("R\u00e9union de rentr\u00e9e\r\nQuand\u00A0: lundi 5 octobre 2026, 10:00 (Europe/Brussels)\r\n", MimeWire.TextOf(text));
        Assert.Contains("O\u00f9\u00A0: Salle 2", MimeWire.TextOf(text));
        Assert.Contains("Organis\u00e9 par Alice", MimeWire.TextOf(text));
        var calendar = Assert.IsType<TextPart>(alternative[1]);
        Assert.Equal("text/calendar", calendar.ContentType.MimeType);
        Assert.Equal("REQUEST", calendar.ContentType.Parameters["method"]);
        Assert.Equal("utf-8", calendar.ContentType.Charset);
        Assert.Equal(IcsMethod.With(Stored, "REQUEST"), MimeWire.TextOf(calendar));
        var attachment = Assert.IsType<MimePart>(mixed[1]);
        Assert.Equal("application/ics", attachment.ContentType.MimeType);
        Assert.Equal("invitation.ics", attachment.FileName);
        Assert.True(attachment.IsAttachment);
        Assert.Equal(MimeWire.TextOf(calendar), Attached(message));
    }

    [Fact]
    public void Request_LeavesTheReplyStampsAtHome_AndEveryOtherByteAsStored()
    {
        const string Marc = "ATTENDEE;CN=Marc Dupont;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:marc.dupont@example.org";
        var stamped = Marc.Replace(";ROLE=", ";X-WEESKY-REPLY-STAMP=20260913T100000Z;ROLE=");
        var stored = Stored.Replace(Marc, string.Join("\r\n", ItipCalendar.Fold(stamped)));
        Assert.Contains("X-WEESKY-REPLY-STAMP", stored);

        var message = InvitationMailer.Compose(Input(MailKind.Update, "julie@example.net") with { StoredIcs = stored });

        var calendar = MimeWire.TextOf(Alternative(message).OfType<TextPart>().Last());
        Assert.DoesNotContain("X-WEESKY-REPLY-STAMP", calendar);
        Assert.Equal(calendar, Attached(message));
        var line = PartStatRewriter.Unfold(calendar).Single(l => l.Text.EndsWith("mailto:marc.dupont@example.org", StringComparison.Ordinal));
        Assert.Equal(Marc, line.Text);
        Assert.True(line.Count > 1, "the line is long enough to be folded");
        Assert.All(calendar.Split("\r\n").Skip(line.First).Take(line.Count), physical => Assert.True(physical.Length <= 75, physical));
        Assert.Equal(IcsMethod.With(Stored, "REQUEST"), string.Join("\r\n", calendar.Split("\r\n").Take(line.First)
            .Append(Marc).Concat(calendar.Split("\r\n").Skip(line.First + line.Count))));
        Assert.DoesNotContain("X-WEESKY-REPLY-STAMP", InvitationMailer.Calendar(Input(MailKind.Cancellation, "marc.dupont@example.org") with { StoredIcs = stored }));
    }

    [Fact]
    public void Update_SaysSo_AndCarriesTheSameFile()
    {
        var message = InvitationMailer.Compose(Input(MailKind.Update, "marc.dupont@example.org") with { Language = "en" });

        Assert.Equal("Updated: R\u00e9union de rentr\u00e9e", message.Subject);
        var calendar = Alternative(message).OfType<TextPart>().Last();
        Assert.Equal("REQUEST", calendar.ContentType.Parameters["method"]);
        Assert.Equal(IcsMethod.With(Stored, "REQUEST"), MimeWire.TextOf(calendar));
        Assert.EndsWith("\r\n\r\nReply from your calendar, or by return mail.\r\n", MimeWire.TextOf(Alternative(message).OfType<TextPart>().First()));
    }

    [Fact]
    public void Cancel_IsAReducedFile_ToTheRecipientsOnly_WithTheGivenSequence()
    {
        var message = InvitationMailer.Compose(Input(MailKind.Cancellation, "julie@example.net") with { CancelSequence = 2 });

        Assert.Equal("Annulation\u00A0: R\u00e9union de rentr\u00e9e", message.Subject);
        var calendar = Alternative(message).OfType<TextPart>().Last();
        Assert.Equal("CANCEL", calendar.ContentType.Parameters["method"]);
        Assert.Equal(MimeWire.TextOf(calendar), InvitationMailer.Calendar(Input(MailKind.Cancellation, "julie@example.net") with { CancelSequence = 2 }));
        Assert.EndsWith("\r\nEND:VCALENDAR\r\n", MimeWire.TextOf(calendar));
        var lines = MimeWire.TextOf(calendar).Split("\r\n");
        Assert.Equal("BEGIN:VCALENDAR", lines[0]);
        Assert.Contains("METHOD:CANCEL", lines);
        Assert.Contains("UID:web-1111-2222", lines);
        Assert.Contains("SEQUENCE:2", lines);
        Assert.Contains("DTSTAMP:20260913T100000Z", lines);
        Assert.Contains("ORGANIZER;CN=Alice:mailto:alice@weesky.be", lines);
        Assert.Contains("ATTENDEE:mailto:julie@example.net", lines);
        Assert.DoesNotContain(lines, l => l.Contains("marc.dupont", StringComparison.Ordinal));
        Assert.Contains("DTSTART;TZID=Europe/Brussels:20261005T100000", lines);
        Assert.Contains("SUMMARY:R\u00e9union de rentr\u00e9e", lines);
        Assert.Contains("STATUS:CANCELLED", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("RRULE", StringComparison.Ordinal) || l.StartsWith("LOCATION", StringComparison.Ordinal));
        Assert.Contains("Ce rendez-vous est annul\u00e9.", MimeWire.TextOf(Assert.IsType<TextPart>(Alternative(message)[0])));
        Assert.Equal(MimeWire.TextOf(calendar), Attached(message));
    }

    [Fact]
    public void Cancel_EscapesTheSummary_AndQuotesTheOrganizerName()
    {
        var stored = Stored.Replace("SUMMARY:R\u00e9union de rentr\u00e9e", "SUMMARY:R\u00e9union\\, rentr\u00e9e", StringComparison.Ordinal);
        var input = Input(MailKind.Cancellation, "julie@example.net") with { StoredIcs = stored, Organizer = new("alice@weesky.be", "Martin, Alice") };

        var lines = InvitationMailer.Calendar(input).Split("\r\n");

        Assert.Contains("SUMMARY:R\u00e9union\\, rentr\u00e9e", lines);
        Assert.Contains("ORGANIZER;CN=\"Martin, Alice\":mailto:alice@weesky.be", lines);
    }

    [Fact]
    public void WithoutAnOrganizerName_TheAddressStandsForIt()
    {
        var message = InvitationMailer.Compose(Input(MailKind.Cancellation, "julie@example.net") with { Organizer = new("alice@weesky.be", null) });

        Assert.Equal(string.Empty, message.From.Mailboxes.Single().Name);
        Assert.Contains("Organis\u00e9 par alice@weesky.be", MimeWire.TextOf(Assert.IsType<TextPart>(Alternative(message)[0])));
        Assert.Contains("ORGANIZER:mailto:alice@weesky.be", MimeWire.TextOf(Alternative(message).OfType<TextPart>().Last()).Split("\r\n"));
    }

    [Theory]
    [InlineData("TZID=\"W. Europe Standard Time\"", null, "lundi 5 octobre 2026, 10:00 (Europe/Berlin)")]
    [InlineData("TZID=America/New_York", null, "lundi 5 octobre 2026, 10:00 (America/New_York)")]
    [InlineData("TZID=\"(UTC+01:00) Amsterdam, Berlin, Bern, Rome, Stockholm, Vienna\"",
        "(UTC+01:00) Amsterdam, Berlin, Bern, Rome, Stockholm, Vienna", "lundi 5 octobre 2026, 09:00 (UTC)")]
    public void When_IsReadInTheEventsOwnZone_WhateverItsSpelling(string tzid, string? ownBlock, string expected)
    {
        var stored = Stored.Replace("TZID=Europe/Brussels", tzid, StringComparison.Ordinal);
        if (ownBlock is not null)
            stored = stored.Replace("BEGIN:VEVENT", Ics.FixedZone(ownBlock, "+0100") + "BEGIN:VEVENT", StringComparison.Ordinal);

        var message = InvitationMailer.Compose(Input(MailKind.Invitation, "julie@example.net") with { StoredIcs = stored });

        Assert.Contains("\r\nQuand\u00A0: " + expected + "\r\n", MimeWire.TextOf(Assert.IsType<TextPart>(Alternative(message)[0])));
    }

    [Theory]
    [InlineData("DTSTART:20261005T080000Z", "DTEND:20261005T090000Z", "lundi 5 octobre 2026, 08:00 (UTC)")]
    [InlineData("DTSTART:20261005T100000", "DTEND:20261005T110000", "lundi 5 octobre 2026, 10:00")]
    [InlineData("DTSTART;VALUE=DATE:20261005", "DTEND;VALUE=DATE:20261006", "lundi 5 octobre 2026, journ\u00e9e enti\u00e8re")]
    public void When_NamesUtc_ButNeitherAFloatingTimeNorAWholeDay(string start, string end, string expected)
    {
        var stored = Stored
            .Replace("DTSTART;TZID=Europe/Brussels:20261005T100000", start, StringComparison.Ordinal)
            .Replace("DTEND;TZID=Europe/Brussels:20261005T110000", end, StringComparison.Ordinal);

        var message = InvitationMailer.Compose(Input(MailKind.Invitation, "julie@example.net") with { StoredIcs = stored });

        Assert.Contains("\r\nQuand\u00A0: " + expected + "\r\n", MimeWire.TextOf(Assert.IsType<TextPart>(Alternative(message)[0])));
    }

    // The one definition of a deliverable recipient, which the scheduler filters with and the composer
    // writes To with: MimeKit's own constructor, and an address that holds an '@'.
    [Theory]
    [InlineData("user@xn--bcher-kva.de", "user@bücher.de")]
    [InlineData("user@example.com.", "user@example.com")]
    [InlineData("a@b.org (Foo)", "a@b.org")]
    [InlineData("a@b.org>", null)]
    [InlineData("(c) a@b.org", null)]
    [InlineData("foo", null)]
    [InlineData("<a@b>", null)]
    [InlineData("Foo <a@b>", null)]
    [InlineData("mailto:a@b.org", null)]
    [InlineData("@example.org", null)]
    public void Mailbox_IsTheComposersConstructor_WithAnAt(string recipient, string? address)
    {
        Assert.Equal(address, InvitationMailer.Mailbox(recipient)?.Address);
        if (address is null) Assert.Throws<ArgumentException>(() => InvitationMailer.Compose(Input(MailKind.Invitation, recipient)));
    }
}
