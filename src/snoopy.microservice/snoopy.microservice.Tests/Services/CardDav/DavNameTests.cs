using weesky.Snoopy.Microservice.Services.CardDav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CardDav;

public sealed class DavNameTests
{
    [Theory]
    [InlineData("card.vcf")]
    [InlineData("card")]                        // the suffix is a client convention, not a rule
    [InlineData("un nom avec des espaces.vcf")] // an inner space is legitimate and carried
    [InlineData("urn:uuid:aaaa.vcf")]           // an import keeps the source UID verbatim
    [InlineData("é#?.vcf")]                     // the client may choose these; the segment escapes them
    public void AName_ThatAClientMayChoose_IsAccepted(string name) =>
        Assert.True(DavName.IsValid(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b.vcf")]
    [InlineData("a\\b.vcf")]
    [InlineData("a\u0000b.vcf")]
    [InlineData("a\u001Fb.vcf")]
    [InlineData("a\u007Fb.vcf")]
    [InlineData(" leading.vcf")]
    [InlineData("trailing.vcf ")]
    public void AName_ThatWouldBreakSomething_IsRefused(string? name) =>
        Assert.False(DavName.IsValid(name));

    [Fact]
    public void ANameOfTwoHundredAndFiftyFiveCharacters_IsAccepted() =>
        Assert.True(DavName.IsValid(new string('a', 255)));

    [Fact]
    public void ANameOfTwoHundredAndFiftySix_IsRefused() =>
        Assert.False(DavName.IsValid(new string('a', 256)));

    [Fact]
    public void EdgeSpaces_AreRefusedBecauseTheCollationPadsThem()
    {
        // utf8mb4_bin settles case but not space: it is PAD SPACE under MariaDB, so "carte.vcf"
        // and "carte.vcf " are equal for the unique index while being two distinct URLs for every
        // HTTP client. A uniqueness comparison that merges two resources is worse than one that
        // separates them: the second PUT would fail on a duplicate the client can neither
        // understand nor correct.
        Assert.False(DavName.IsValid("carte.vcf "));
        Assert.True(DavName.IsValid("carte .vcf"));
    }

    [Fact]
    public void ForContact_SpellsTheNameTheWebmailHasAlwaysWritten()
    {
        var contactId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // Three call sites in ContactStore spelled this literally before the helper existed; the
        // rows they already wrote must keep matching what the helper produces.
        Assert.Equal("11111111-1111-1111-1111-111111111111.vcf", DavName.ForContact(contactId));
        Assert.Equal($"{contactId}.vcf", DavName.ForContact(contactId));
        Assert.True(DavName.IsValid(DavName.ForContact(Guid.NewGuid())));
    }
}
