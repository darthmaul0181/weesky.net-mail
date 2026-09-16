using weesky.Scotty.Microservice.Services.Calendar.Invitations;
using weesky.Scotty.Microservice.Models.Calendar;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services.Calendar.Invitations;

public sealed class InvitationTextTests
{
    private static ParsedInvitation Google => InvitationParser.Read(InvitationParserTests.Fixture("google-request")).Invitation!;
    private static ParsedInvitation Apple => InvitationParser.Read(InvitationParserTests.Fixture("apple-request")).Invitation!;

    [Fact]
    public void When_InTheBrowserZone_InFrenchAndInEnglish()
    {
        Assert.Equal("samedi 10 octobre 2026, 19:30", InvitationText.When(Google, "Europe/Brussels", "fr"));
        Assert.Equal("Saturday 10 October 2026, 19:30", InvitationText.When(Google, "Europe/Brussels", "en"));
        Assert.Equal("Saturday 10 October 2026, 13:30", InvitationText.When(Google, "America/New_York", "en"));
    }

    [Fact]
    public void When_OnAWholeDay_IsTheDateAlone()
    {
        Assert.Equal("dimanche 1 novembre 2026, journ\u00e9e enti\u00e8re", InvitationText.When(Apple, "Europe/Brussels", "fr"));
        Assert.Equal("Sunday 1 November 2026, all day", InvitationText.When(Apple, "Europe/Brussels", "en"));
    }

    [Fact]
    public void When_OnAWholeDayRunningSeveralDays_NamesBothEnds()
    {
        var twoDays = Apple with { EndDateExclusive = new DateOnly(2026, 11, 3) };

        Assert.Equal("dimanche 1 novembre 2026 au lundi 2 novembre 2026",
            InvitationText.When(twoDays, "Europe/Brussels", "fr"));
        Assert.Equal("Sunday 1 November 2026 \u2013 Monday 2 November 2026",
            InvitationText.When(twoDays, "Europe/Brussels", "en"));
    }

    [Fact]
    public void Body_NamesBothEndsOfAMultiDayWholeDay()
    {
        var when = InvitationText.When(Apple with { EndDateExclusive = new DateOnly(2026, 11, 3) }, "Europe/Brussels", "fr");

        Assert.Equal("Alice a accepté l'invitation « Toussaint » "
            + "du dimanche 1 novembre 2026 au lundi 2 novembre 2026.",
            InvitationText.Body("Alice", "ACCEPTED", "Toussaint", when, "fr"));
    }

    [Fact]
    public void When_FallsBackToUtc_OnAnUnknownZone()
    {
        Assert.Equal("Saturday 10 October 2026, 17:30", InvitationText.When(Google, "Mars/Olympus", "en"));
    }

    [Fact]
    public void Subject_UsesFrenchTypography()
    {
        Assert.Equal("Accept\u00e9\u00A0: D\u00eener chez Marc", InvitationText.Subject("ACCEPTED", "D\u00eener chez Marc", "fr"));
        Assert.Equal("Provisoire\u00A0: D\u00eener chez Marc", InvitationText.Subject("TENTATIVE", "D\u00eener chez Marc", "fr"));
        Assert.Equal("Refus\u00e9\u00A0: (sans titre)", InvitationText.Subject("DECLINED", null, "fr"));
        Assert.Equal("Declined: (no title)", InvitationText.Subject("DECLINED", "", "en"));
        Assert.Equal("Tentative: Dinner", InvitationText.Subject("TENTATIVE", "Dinner", "de"));
    }

    [Fact]
    public void Body_IsOneSentence()
    {
        Assert.Equal("Alice Martin a accept\u00e9 l'invitation \u00ab\u00A0D\u00eener chez Marc\u00A0\u00bb du samedi 10 octobre 2026, 19:30.",
            InvitationText.Body("Alice Martin", "ACCEPTED", "D\u00eener chez Marc", "samedi 10 octobre 2026, 19:30", "fr"));
        Assert.Equal("Alice Martin has declined the invitation \u201CDinner\u201D on Saturday 10 October 2026, 19:30.",
            InvitationText.Body("Alice Martin", "DECLINED", "Dinner", "Saturday 10 October 2026, 19:30", "en"));
        Assert.Equal("alice@weesky.be a r\u00e9pondu peut-\u00eatre \u00e0 l'invitation \u00ab\u00A0D\u00eener\u00A0\u00bb du lundi.",
            InvitationText.Body("alice@weesky.be", "TENTATIVE", "D\u00eener", "lundi", "fr"));
    }

    [Theory]
    [InlineData(nameof(MailKind.Invitation), "fr", "Invitation\u00A0: D\u00eener")]
    [InlineData(nameof(MailKind.Update), "fr", "Mise \u00e0 jour\u00A0: D\u00eener")]
    [InlineData(nameof(MailKind.Cancellation), "fr", "Annulation\u00A0: D\u00eener")]
    [InlineData(nameof(MailKind.Invitation), "en", "Invitation: D\u00eener")]
    [InlineData(nameof(MailKind.Update), "en", "Updated: D\u00eener")]
    [InlineData(nameof(MailKind.Cancellation), "en", "Cancelled: D\u00eener")]
    [InlineData(nameof(MailKind.Update), "de", "Updated: D\u00eener")]
    public void OrganizerSubject_NamesTheKind_ThenTheTitle(string kind, string language, string expected) =>
        Assert.Equal(expected, InvitationText.OrganizerSubject(Enum.Parse<MailKind>(kind), "D\u00eener", language));

    [Fact]
    public void OrganizerSubject_WithoutTitle_SaysSo()
    {
        Assert.Equal("Invitation\u00A0: (sans titre)", InvitationText.OrganizerSubject(MailKind.Invitation, null, "fr"));
        Assert.Equal("Invitation: (no title)", InvitationText.OrganizerSubject(MailKind.Invitation, " ", "en"));
    }

    [Fact]
    public void OrganizerSubject_KeepsATitleOnOneLine()
    {
        Assert.Equal("Updated: D\u00eener chez Marc", InvitationText.OrganizerSubject(MailKind.Update, " D\u00eener\r\nchez Marc ", "en"));
    }

    [Fact]
    public void OrganizerBody_ListsTitle_When_Where_Organizer_AndHowToAnswer()
    {
        var body = InvitationText.OrganizerBody(MailKind.Invitation, "D\u00eener", "lundi 5 octobre 2026 \u00b7 10:00 \u2013 11:00 (Europe/Brussels)", "Salle 2", "Alice", "fr");
        Assert.Equal(["D\u00eener", "Quand\u00A0: lundi 5 octobre 2026 \u00b7 10:00 \u2013 11:00 (Europe/Brussels)", "O\u00f9\u00A0: Salle 2", "Organis\u00e9 par Alice", "",
            "R\u00e9pondez depuis votre agenda, ou par retour de mail."], body.Split("\r\n"));

        var cancelled = InvitationText.OrganizerBody(MailKind.Cancellation, "D\u00eener", "\u2026", null, "Alice", "en");
        Assert.Equal(["D\u00eener", "When: \u2026", "Organised by Alice", "", "This event is cancelled."], cancelled.Split("\r\n"));
    }

    [Fact]
    public void OrganizerBody_TheOtherTwoClosings_AndALocationOnOneLine()
    {
        var update = InvitationText.OrganizerBody(MailKind.Update, null, "Monday", "Room\n2", "alice@weesky.be", "en");
        Assert.Equal(["(no title)", "When: Monday", "Where: Room 2", "Organised by alice@weesky.be", "", "Reply from your calendar, or by return mail."],
            update.Split("\r\n"));

        var cancelled = InvitationText.OrganizerBody(MailKind.Cancellation, " ", "lundi", " ", "Alice", "fr");
        Assert.Equal(["(sans titre)", "Quand\u00A0: lundi", "Organis\u00e9 par Alice", "", "Ce rendez-vous est annul\u00e9."], cancelled.Split("\r\n"));
    }
}
