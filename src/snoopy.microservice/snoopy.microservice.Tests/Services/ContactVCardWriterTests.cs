using weesky.Snoopy.Microservice.Services.Contacts;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ContactVCardWriterTests
{
    private static ContactCsvRow Row(Dictionary<string, string> extras, string? first = "Bruno",
        string? last = "Mertens", string? nick = null, params string[] addresses) =>
        new(2, first, last, nick, false, addresses, [], [], extras);

    // A card repeating the columns next to it is a MEDIUMTEXT per contact with nothing to read back.
    [Fact]
    public void Write_AnswersNullWhenNothingIsOutsideTheModel()
    {
        Assert.Null(ContactVCardWriter.Write(Row([], addresses: "bruno@example.com")));
    }

    [Fact]
    public void Write_KeepsThePhones()
    {
        var card = ContactVCardWriter.Write(Row(new()
        {
            ["mobilephone"] = "+32470000000",
            ["homephone"] = "+3281000000",
            ["businessfax"] = "+3281000001",
        }))!;

        Assert.Contains("TEL;TYPE=CELL:+32470000000", card);
        Assert.Contains("TEL;TYPE=HOME,VOICE:+3281000000", card);
        Assert.Contains("TEL;TYPE=WORK,FAX:+3281000001", card);
    }

    [Fact]
    public void Write_KeepsTheOrganisationAndRole()
    {
        var card = ContactVCardWriter.Write(Row(new()
        {
            ["company"] = "Weesky", ["department"] = "Support", ["jobtitle"] = "Engineer",
        }))!;

        Assert.Contains("ORG:Weesky;Support", card);
        Assert.Contains("TITLE:Engineer", card);
    }

    [Fact]
    public void Write_KeepsThePostalAddresses()
    {
        var card = ContactVCardWriter.Write(Row(new()
        {
            ["homestreet"] = "Rue X 1", ["homecity"] = "Namur",
            ["homepostalcode"] = "5000", ["homecountry"] = "Belgium",
            ["businessstreet"] = "Rue Y 2", ["officelocation"] = "Room 3",
        }))!;

        Assert.Contains("ADR;TYPE=HOME:;;Rue X 1;Namur;;5000;Belgium", card);
        Assert.Contains("ADR;TYPE=WORK:;Room 3;Rue Y 2;;;;", card);
    }

    [Fact]
    public void Write_KeepsTheRemainingScalars()
    {
        var card = ContactVCardWriter.Write(Row(new()
        {
            ["notes"] = "a note", ["birthday"] = "1980-01-15", ["webpage"] = "https://x.be",
        }))!;

        Assert.Contains("NOTE:a note", card);
        Assert.Contains("BDAY:1980-01-15", card);
        Assert.Contains("URL:https://x.be", card);
    }

    // Outlook's "Title" is the honorific and its "Job Title" the role; the honorific has no property
    // of its own, it is N's fourth component.
    [Fact]
    public void Write_PutsTheMiddleNameAndHonorificInTheStructuredName()
    {
        var card = ContactVCardWriter.Write(Row(new() { ["middlename"] = "J", ["title"] = "Mr" }))!;

        Assert.Contains("N:Mertens;Bruno;J;Mr;", card);
        Assert.Contains("FN:Bruno J Mertens", card);
    }

    [Fact]
    public void Write_EmitsAWellFormedCardWithTheModelledFields()
    {
        var card = ContactVCardWriter.Write(
            Row(new() { ["notes"] = "x" }, nick: "bruno", addresses: "bruno@example.com"))!;

        Assert.StartsWith("BEGIN:VCARD\r\nVERSION:3.0\r\n", card);
        Assert.EndsWith("END:VCARD\r\n", card);
        Assert.Contains("NICKNAME:bruno", card);
        Assert.Contains("EMAIL;TYPE=INTERNET:bruno@example.com", card);
    }

    [Fact]
    public void Write_EscapesTheSeparators()
    {
        var card = ContactVCardWriter.Write(Row(new() { ["notes"] = "a; b, c\\d\ne" }))!;

        Assert.Contains(@"NOTE:a\; b\, c\\d\ne", card);
    }
}
