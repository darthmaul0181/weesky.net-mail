using System.Text;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Contacts;
using weesky.Snoopy.Microservice.Services.Csv;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ContactCsvMapperTests
{
    private static IReadOnlyList<ContactCsvRow> Map(string csv)
    {
        var mapped = ContactCsvMapper.Map(CsvReader.Read(new UTF8Encoding(false).GetBytes(csv)));
        Assert.True(mapped.IsSuccess);
        return mapped.Value;
    }

    // The real Snappymail/Rainloop export, which is Outlook's column set.
    private const string RainloopHeader =
        "Title,First Name,Middle Name,Last Name,Nick Name,Display Name,Company,Department,Job Title," +
        "Office Location,E-mail Address,Notes,Web Page,Birthday,Other Email,Other Phone,Other Mobile," +
        "Mobile Phone,Home Email,Home Phone,Home Fax,Home Street,Home City,Home State,Home Postal Code," +
        "Home Country,Business Email,Business Phone,Business Fax,Business Street,Business City," +
        "Business State,Business Postal Code,Business Country";

    [Fact]
    public void Map_ReadsTheRainloopExport()
    {
        var row = Assert.Single(Map(RainloopHeader + "\r\n" +
            "Mr,Bruno,J,Mertens,bruno,Bruno Mertens,Weesky,Support,Engineer,Room 3," +
            "bruno@example.com,a note,https://x.be,1980-01-15,other@example.com,,,+32470000000," +
            "home@example.com,,,,,,,,,,,,,,,"));

        Assert.Equal("Bruno", row.FirstName);
        Assert.Equal("Mertens", row.LastName);
        Assert.Equal("bruno", row.Nickname);
        Assert.Equal(["bruno@example.com", "other@example.com", "home@example.com"], row.Addresses);
    }

    [Fact]
    public void Map_ReadsTheGoogleExport()
    {
        var row = Assert.Single(Map(
            "Given Name,Family Name,E-mail 1 - Value,E-mail 2 - Value\r\n" +
            "Bruno,Mertens,bruno@example.com,second@example.com"));

        Assert.Equal("Bruno", row.FirstName);
        Assert.Equal(["bruno@example.com", "second@example.com"], row.Addresses);
    }

    // Google numbers past its usual two address columns just like our own exporter does, and a
    // finite list of literals would silently drop the fifth address into Extras.
    [Fact]
    public void Map_ReadsAGoogleExportWithMoreThanFourAddresses()
    {
        var row = Assert.Single(Map(
            "E-mail 1 - Value,E-mail 2 - Value,E-mail 3 - Value,E-mail 4 - Value,E-mail 5 - Value\r\n" +
            "a@example.com,b@example.com,c@example.com,d@example.com,e@example.com"));

        Assert.Equal(
            ["a@example.com", "b@example.com", "c@example.com", "d@example.com", "e@example.com"],
            row.Addresses);
    }

    [Fact]
    public void Map_ReadsTheThunderbirdExport()
    {
        var row = Assert.Single(Map(
            "First Name,Last Name,Nickname,Primary Email,Secondary Email\r\n" +
            "Bruno,Mertens,bruno,bruno@example.com,second@example.com"));

        Assert.Equal("bruno", row.Nickname);
        Assert.Equal(["bruno@example.com", "second@example.com"], row.Addresses);
    }

    // Our own export numbers its extra address columns, and how many there are depends on the book
    // it came from — so a finite list would cap what we can read back from ourselves.
    [Fact]
    public void Map_ReadsOurOwnNumberedAddressColumns()
    {
        var row = Assert.Single(Map(
            "First Name,E-mail Address,E-mail 2 Address,E-mail 7 Address,Favorite\r\n" +
            "Bruno,a@example.com,b@example.com,c@example.com,true"));

        Assert.Equal(["a@example.com", "b@example.com", "c@example.com"], row.Addresses);
        Assert.True(row.IsFavorite);
    }

    [Theory]
    [InlineData("FIRST NAME")]
    [InlineData("first_name")]
    [InlineData("First-Name")]
    public void Map_IgnoresCaseAndSeparatorsInHeaders(string header)
    {
        Assert.Equal("Bruno", Assert.Single(Map($"{header}\r\nBruno")).FirstName);
    }

    // Position 0 is the primary, and the file's own column order is what decides it — no column is
    // named "the primary one".
    [Fact]
    public void Map_TakesAddressesInColumnOrder()
    {
        var row = Assert.Single(Map(
            "Other Email,E-mail Address\r\nother@example.com,main@example.com"));

        Assert.Equal(["other@example.com", "main@example.com"], row.Addresses);
    }

    [Fact]
    public void Map_DropsAnUnparsableAddressAndReportsIt()
    {
        var row = Assert.Single(Map(
            "First Name,E-mail Address,Other Email\r\nBruno,n/a,bruno@example.com"));

        Assert.Equal(["bruno@example.com"], row.Addresses);
        Assert.Equal(["n/a"], row.RejectedAddresses);
    }

    [Fact]
    public void Map_FoldsAnAddressRepeatedAcrossColumns()
    {
        var row = Assert.Single(Map(
            "E-mail Address,Home Email\r\nBruno@Example.com,bruno@example.com"));

        Assert.Equal(["Bruno@Example.com"], row.Addresses);
    }

    // Splitting it on a space would be guessing, and wrong on every compound name. The nickname is
    // exactly where displayNameOf looks next.
    [Fact]
    public void Map_UsesTheDisplayNameOnlyWhenNoNameAtAll()
    {
        var withName = Assert.Single(Map("First Name,Display Name\r\nBruno,Bruno Mertens"));
        var without = Assert.Single(Map("Display Name,E-mail Address\r\nBruno Mertens,b@example.com"));

        Assert.Null(withName.Nickname);
        Assert.Equal("Bruno Mertens", without.Nickname);
    }

    [Fact]
    public void Map_KeepsUnmodelledColumnsAsExtras()
    {
        var row = Assert.Single(Map(
            "First Name,Mobile Phone,Company,Empty Column\r\nBruno,+32470000000,Weesky,"));

        Assert.Equal("+32470000000", row.Extras["mobilephone"]);
        Assert.Equal("Weesky", row.Extras["company"]);
        Assert.False(row.Extras.ContainsKey("emptycolumn"));
    }

    // A recognised name column keeps the first non-empty value; an unmodelled one must not
    // silently overwrite it with the last, losing the earlier value with no trace.
    [Fact]
    public void Map_KeepsTheFirstValueOfADuplicatedUnmodelledColumn()
    {
        var row = Assert.Single(Map("First Name,Company,Company\r\nBruno,First,Second"));

        Assert.Equal("First", row.Extras["company"]);
    }

    // It catches the file read with the wrong delimiter, the one with no header row, and the one
    // that is not a CSV at all.
    [Fact]
    public void Map_RefusesAFileWithNoRecognisedColumn()
    {
        var mapped = ContactCsvMapper.Map(
            CsvReader.Read(new UTF8Encoding(false).GetBytes("Alpha,Beta\r\n1,2")));

        Assert.True(mapped.IsFailure);
        Assert.Equal(ContactCsvMapper.NoRecognisedColumn, mapped.Error);
    }

    [Fact]
    public void Map_ToleratesARowShorterThanTheHeader()
    {
        var row = Assert.Single(Map("First Name,Last Name,E-mail Address\r\nBruno"));

        Assert.Equal("Bruno", row.FirstName);
        Assert.Empty(row.Addresses);
    }

    // The column widths this mirrors (contacts.first_name VARCHAR(100)) are what turns an unbounded
    // value into a strict-mode MariaDB 500 — dropped here, not truncated, so a column shift never
    // stores a silent fragment of someone's free text as a name.
    [Fact]
    public void Map_DropsAnOverLongNameAndReportsIt()
    {
        var tooLong = new string('x', ContactValidator.MaxNameLength + 1);
        var row = Assert.Single(Map($"First Name,E-mail Address\r\n{tooLong},bruno@example.com"));

        Assert.Null(row.FirstName);
        Assert.Equal(["first name"], row.OverLongFields);
        Assert.Equal(["bruno@example.com"], row.Addresses);
    }

    [Fact]
    public void Map_DropsAnOverLongAddressAndReportsIt()
    {
        var tooLong = new string('a', ContactValidator.MaxAddressLength + 1) + "@example.com";
        var row = Assert.Single(Map($"First Name,E-mail Address\r\nBruno,{tooLong}"));

        Assert.Empty(row.Addresses);
        Assert.Equal([tooLong], row.RejectedAddresses);
    }

    // The over-long display name falls back to nickname the same way a normal one does, so it must
    // be capped there too — not only when it arrives through its own column.
    [Fact]
    public void Map_CapsTheDisplayNameFallbackTheSameWayAsANickname()
    {
        var tooLong = new string('x', ContactValidator.MaxNameLength + 1);
        var row = Assert.Single(Map($"Display Name,E-mail Address\r\n{tooLong},bruno@example.com"));

        Assert.Null(row.Nickname);
        Assert.Equal(["nickname"], row.OverLongFields);
    }

    // The exporter's extended Outlook header set (task 7) lands on exactly the extras keys
    // ContactsController.WriteOf/Postal already read: symmetry falls out of the same normalisation
    // rule this file pins everywhere else, with no column-specific code of its own.
    [Fact]
    public void Map_KeepsTheExtendedOutlookColumnsAsTheExtrasTheExporterNowWrites()
    {
        var row = Assert.Single(Map(
            "First Name,Mobile Phone,Home Phone,Business Phone,Home Fax,Business Fax,Other Phone," +
            "Home Street,Home City,Home State,Home Postal Code,Home Country," +
            "Business Street,Business City,Business State,Business Postal Code,Business Country\r\n" +
            "Bruno,+1,+2,+3,+4,+5,+6,S,C,ST,PC,CO,BS,BC,BST,BPC,BCO"));

        Assert.Equal("+1", row.Extras["mobilephone"]);
        Assert.Equal("+2", row.Extras["homephone"]);
        Assert.Equal("+3", row.Extras["businessphone"]);
        Assert.Equal("+4", row.Extras["homefax"]);
        Assert.Equal("+5", row.Extras["businessfax"]);
        Assert.Equal("+6", row.Extras["otherphone"]);
        Assert.Equal("S", row.Extras["homestreet"]);
        Assert.Equal("PC", row.Extras["homepostalcode"]);
        Assert.Equal("BPC", row.Extras["businesspostalcode"]);
        Assert.Equal("BCO", row.Extras["businesscountry"]);
    }

    // What the store's own NoNameOrAddress path relies on: a row whose only content was an
    // over-long name must come out looking exactly like an empty row, not a truncated one.
    [Fact]
    public void Map_LeavesARowWithOnlyAnOverLongNameAsEmptyAsOneWithNoNameAtAll()
    {
        var tooLong = new string('x', ContactValidator.MaxNameLength + 1);
        var row = Assert.Single(Map($"First Name\r\n{tooLong}"));

        Assert.Null(row.FirstName);
        Assert.Null(row.LastName);
        Assert.Null(row.Nickname);
        Assert.Empty(row.Addresses);
        Assert.Equal(["first name"], row.OverLongFields);
    }
}
