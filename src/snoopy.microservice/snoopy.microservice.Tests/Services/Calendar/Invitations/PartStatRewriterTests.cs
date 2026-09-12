using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;

public sealed class PartStatRewriterTests
{
    private static string Google => InvitationParserTests.Fixture("google-request");

    [Fact]
    public void RewritesOnlyTheUsersAttendeeLine_AndDropsMethod()
    {
        var rewritten = PartStatRewriter.Rewrite(Google, "alice@weesky.be", "ACCEPTED")!;

        Assert.DoesNotContain("METHOD:", rewritten);
        Assert.DoesNotContain("RSVP=TRUE\r\n ;CN=Alice", rewritten);
        Assert.Contains("PARTSTAT=ACCEPTED", rewritten);
        // Marc's and Jean's lines, folded as Google folded them, are untouched.
        Assert.Contains("PARTSTAT=ACCEPTED;CN=Marc Dupont\r\n ;X-NUM-GUESTS=0:mailto:marc.dupont@example.org", rewritten);
        Assert.Contains("RSVP=TRUE\r\n ;CN=jean@example.net", rewritten);
        // Everything else byte for byte: the file minus METHOD and minus Alice's line equals the rewrite minus Alice's line.
        Assert.Equal(WithoutLine(Google.Replace("METHOD:REQUEST\r\n", ""), "mailto:Alice@Weesky.be"),
            WithoutLine(rewritten, "mailto:Alice@Weesky.be"));
    }

    [Fact]
    public void TheRewrittenLine_KeepsTheOtherParameters_AndFoldsAt75()
    {
        var rewritten = PartStatRewriter.Rewrite(Google, "ALICE@weesky.be", "TENTATIVE")!;
        var target = PartStatRewriter.Unfold(rewritten)
            .Single(l => l.Text.EndsWith("mailto:Alice@Weesky.be", StringComparison.Ordinal));

        Assert.Equal("ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;CN=Alice;X-NUM-GUESTS=0;PARTSTAT=TENTATIVE:mailto:Alice@Weesky.be", target.Text);
        // Only the refolded line owes the 75 octets: the fixture carries untouched lines over it.
        Assert.All(rewritten.Split("\r\n").Skip(target.First).Take(target.Count),
            physical => Assert.True(physical.Length <= 75, physical));
        Assert.True(target.Count > 1, "the rewritten line is long enough to have been folded");
    }

    [Fact]
    public void AddsPartStat_WhenTheLineHadNone()
    {
        var ics = Google.Replace("PARTSTAT=NEEDS-ACTION;RSVP=TRUE\r\n ;CN=Alice", "CN=Alice");

        var line = Unfolded(PartStatRewriter.Rewrite(ics, "alice@weesky.be", "DECLINED")!)
            .Single(l => l.EndsWith("mailto:Alice@Weesky.be", StringComparison.Ordinal));

        Assert.EndsWith(";PARTSTAT=DECLINED:mailto:Alice@Weesky.be", line);
    }

    /// <summary>A quoted CN may hold the ':' that would otherwise end the parameters and the ';'
    /// that would otherwise cut one — Outlook writes quoted names.</summary>
    [Fact]
    public void RewritesThroughAQuotedParameter_HoldingBothSeparators()
    {
        var outlook = InvitationParserTests.Fixture("outlook-request").Replace("CN=\"ALICE\"", "CN=\"Alice: chef; RH\"");

        var line = Unfolded(PartStatRewriter.Rewrite(outlook, "alice@weesky.be", "ACCEPTED")!)
            .Single(l => l.EndsWith("mailto:ALICE@WEESKY.BE", StringComparison.Ordinal));

        Assert.Equal("ATTENDEE;ROLE=REQ-PARTICIPANT;CN=\"Alice: chef; RH\";PARTSTAT=ACCEPTED:mailto:ALICE@WEESKY.BE", line);
    }

    [Fact]
    public void IsIdempotent()
    {
        var once = PartStatRewriter.Rewrite(Google, "alice@weesky.be", "ACCEPTED")!;
        Assert.Equal(once, PartStatRewriter.Rewrite(once, "alice@weesky.be", "ACCEPTED"));
    }

    [Fact]
    public void NullWhenTheAddressIsNotInvited()
    {
        Assert.Null(PartStatRewriter.Rewrite(Google, "nobody@weesky.be", "ACCEPTED"));
    }

    /// <summary>A PARTSTAT is spliced into the stored file: anything but a token would let the
    /// answer write lines of its own — an ATTENDEE, an ORGANIZER, a whole component.</summary>
    [Theory]
    [InlineData("ACCEPTED\r\nATTENDEE;PARTSTAT=ACCEPTED:mailto:mallory@example.org")]
    [InlineData("ACCEPTED\nORGANIZER:mailto:mallory@example.org")]
    [InlineData("ACCEPTED;RSVP=FALSE")]
    [InlineData("ACCEPTED:mailto:mallory@example.org")]
    [InlineData("ACCEPTED\"")]
    [InlineData(" ACCEPTED")]
    [InlineData("")]
    public void NullWhenThePartStatIsNotAToken(string partStat)
    {
        Assert.Null(PartStatRewriter.Rewrite(Google, "alice@weesky.be", partStat));
    }

    [Fact]
    public void AnXNameIsAToken()
    {
        var line = Unfolded(PartStatRewriter.Rewrite(Google, "alice@weesky.be", "X-WEESKY-MAYBE")!)
            .Single(l => l.EndsWith("mailto:Alice@Weesky.be", StringComparison.Ordinal));

        Assert.EndsWith(";PARTSTAT=X-WEESKY-MAYBE:mailto:Alice@Weesky.be", line);
    }

    /// <summary>RFC 5546 carries one PARTSTAT per component: an answer absent from an override
    /// would leave the moved date unanswered in the organizer's agenda.</summary>
    [Fact]
    public void RewritesTheAttendeeLineOfEveryComponent()
    {
        var series = InvitationParserTests.Fixture("google-override-first");

        var rewritten = PartStatRewriter.Rewrite(series, "alice@weesky.be", "DECLINED")!;

        var answered = Unfolded(rewritten).Where(l => l.EndsWith("mailto:Alice@Weesky.be", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, answered.Count);
        Assert.All(answered, l => Assert.EndsWith(";PARTSTAT=DECLINED:mailto:Alice@Weesky.be", l));
        // Everything else byte for byte, Marc's two lines and the VALARM included.
        Assert.Equal(WithoutLine(series.Replace("METHOD:REQUEST\r\n", ""), "mailto:Alice@Weesky.be"),
            WithoutLine(rewritten, "mailto:Alice@Weesky.be"));
    }

    [Fact]
    public void LineOf_ReadsTheMaster_WhenAnOverrideIsWrittenFirst()
    {
        var series = InvitationParserTests.Fixture("google-override-first");

        Assert.Equal("DTSTART;TZID=Europe/Brussels:20261010T193000", PartStatRewriter.LineOf(series, "DTSTART"));
        Assert.Null(PartStatRewriter.LineOf(series, "RECURRENCE-ID"));
    }

    /// <summary>A VALARM carries a DESCRIPTION and a TRIGGER of its own, and it is written before
    /// the event's own DESCRIPTION in this file.</summary>
    [Fact]
    public void LineOf_ReadsPastANestedValarm()
    {
        var series = InvitationParserTests.Fixture("google-override-first");

        Assert.Equal("DESCRIPTION:On se retrouve chez moi.", PartStatRewriter.LineOf(series, "DESCRIPTION"));
        Assert.Null(PartStatRewriter.LineOf(series, "TRIGGER"));
    }

    [Fact]
    public void KeepsLfFiles_Lf()
    {
        var lf = Google.Replace("\r\n", "\n");
        var rewritten = PartStatRewriter.Rewrite(lf, "alice@weesky.be", "ACCEPTED")!;
        Assert.DoesNotContain("\r", rewritten);
    }

    [Fact]
    public void StripMethod_RemovesOnlyThatLine()
    {
        Assert.Equal(Google.Replace("METHOD:REQUEST\r\n", ""), PartStatRewriter.StripMethod(Google));
    }

    [Fact]
    public void LineOf_UnfoldsAndFindsTheProperty()
    {
        Assert.Equal("DTSTART;TZID=Europe/Brussels:20261010T193000", PartStatRewriter.LineOf(Google, "DTSTART"));
        Assert.Equal("ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE;CN=\"ALICE\":mailto:ALICE@WEESKY.BE",
            PartStatRewriter.LineOf(InvitationParserTests.Fixture("outlook-request"), "ATTENDEE"));
        Assert.Null(PartStatRewriter.LineOf(Google, "RRULE"));
    }

    /// <summary>A folded line whose continuation would fall inside a multi-byte character.</summary>
    [Fact]
    public void Folds_OnCharacters_NotOnOctets()
    {
        var ics = Google.Replace("CN=Alice;", "CN=Prénom Très Accentué Léonie Gaëlle Ångström;");

        var rewritten = PartStatRewriter.Rewrite(ics, "alice@weesky.be", "ACCEPTED")!;

        var target = PartStatRewriter.Unfold(rewritten)
            .Single(l => l.Text.EndsWith("mailto:Alice@Weesky.be", StringComparison.Ordinal));
        Assert.Contains("CN=Prénom Très Accentué Léonie Gaëlle Ångström", target.Text);
        Assert.All(rewritten.Split("\r\n").Skip(target.First).Take(target.Count),
            physical => Assert.True(System.Text.Encoding.UTF8.GetByteCount(physical) <= 75, physical));
    }

    private static IEnumerable<string> Unfolded(string ics) => PartStatRewriter.Unfold(ics).Select(l => l.Text);

    private static string WithoutLine(string ics, string marker) =>
        string.Join("\r\n", PartStatRewriter.Unfold(ics).Where(l => !l.Text.EndsWith(marker, StringComparison.Ordinal)).Select(l => l.Text));
}
