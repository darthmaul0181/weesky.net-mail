using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;

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
}
