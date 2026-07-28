using System.Text;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Contacts;
using weesky.Snoopy.Microservice.Services.Csv;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ContactCsvExporterTests
{
    private static ContactView Contact(
        string? first = null, string? last = null, string? nick = null,
        bool favorite = false, params string[] addresses) =>
        new(Guid.NewGuid(), first, last, nick, favorite, addresses);

    private static CsvDocument Parse(byte[] content) => CsvReader.Read(content);

    [Fact]
    public void Write_EmitsTheColumnsRainloopUnderstands()
    {
        var document = Parse(ContactCsvExporter.Write(
            [Contact(first: "Bruno", last: "Mertens", addresses: "bruno@example.com")]));

        Assert.Equal(
            ["First Name", "Last Name", "Nick Name", "Display Name", "E-mail Address", "Favorite"],
            document.Header);
        Assert.Equal(
            ["Bruno", "Mertens", "", "Bruno Mertens", "bruno@example.com", ""],
            Assert.Single(document.Rows).Fields);
    }

    // A column empty across the whole file is noise, and a fixed ceiling would lose addresses.
    [Fact]
    public void Write_SizesTheAddressColumnsToTheFullestContact()
    {
        var document = Parse(ContactCsvExporter.Write(
        [
            Contact(first: "A", addresses: "a@example.com"),
            Contact(first: "B", addresses: ["b1@example.com", "b2@example.com", "b3@example.com"]),
        ]));

        Assert.Equal(
            ["E-mail Address", "E-mail 2 Address", "E-mail 3 Address"],
            document.Header.Skip(4).Take(3));
    }

    [Fact]
    public void Write_MarksAFavourite()
    {
        var document = Parse(ContactCsvExporter.Write([Contact(first: "A", favorite: true, addresses: "a@example.com")]));

        Assert.Equal("true", Assert.Single(document.Rows).Fields[^1]);
    }

    // ListAsync hands rows back in no particular order, so two contacts sharing a sort key need a
    // tiebreaker or the row order can drift between two exports of an unchanged book.
    [Fact]
    public void Write_BreaksATieOnDisplayNameByContactId()
    {
        var lower = new ContactView(
            Guid.Parse("00000000-0000-0000-0000-000000000001"), "John", "Smith", null, false, ["lower@example.com"]);
        var higher = new ContactView(
            Guid.Parse("00000000-0000-0000-0000-000000000002"), "John", "Smith", null, false, ["higher@example.com"]);

        var document = Parse(ContactCsvExporter.Write([higher, lower]));

        Assert.Equal(["lower@example.com", "higher@example.com"], document.Rows.Select(r => r.Fields[4]));
    }

    // Written verbatim it would come back as a nickname on the next import, which is not a name the
    // user ever typed.
    [Fact]
    public void Write_LeavesTheDisplayNameEmptyForAnAddressOnlyContact()
    {
        var document = Parse(ContactCsvExporter.Write([Contact(addresses: "a@example.com")]));

        Assert.Equal("", Assert.Single(document.Rows).Fields[3]);
    }

    [Fact]
    public void Write_FallsBackToTheNicknameForTheDisplayName()
    {
        var document = Parse(ContactCsvExporter.Write([Contact(nick: "bruno", addresses: "a@example.com")]));

        Assert.Equal("bruno", Assert.Single(document.Rows).Fields[3]);
    }

    [Fact]
    public void Write_AnswersAHeaderOnlyFileForAnEmptyBook()
    {
        var document = Parse(ContactCsvExporter.Write([]));

        Assert.NotEmpty(document.Header);
        Assert.Empty(document.Rows);
    }

    // The claim the whole slice rests on: what we write, we read back — and reading it back a
    // second time creates nothing. The book carries an address-less contact and a nickname-only one
    // because those are the two the address index alone cannot recognise on the way back.
    [Fact]
    public async Task Write_RoundTripsThroughTheImport()
    {
        var db = nameof(Write_RoundTripsThroughTheImport);
        var user = Guid.NewGuid();
        var store = new ContactStore(new PreferencesTestDbContext(db));
        await store.CreateAsync(user, new ContactWrite("Bruno", "Mertens", "bruno", true,
            ["bruno@example.com", "second@example.com"], "manual"), CancellationToken.None);
        await store.CreateAsync(user, new ContactWrite("Solo", "Sansmail", null, false, [], "manual"),
            CancellationToken.None);
        await store.CreateAsync(user, new ContactWrite(null, null, "zorro", false, [], "manual"),
            CancellationToken.None);

        var book = await new ContactStore(new PreferencesTestDbContext(db)).ListAsync(user, CancellationToken.None);
        var mapped = ContactCsvMapper.Map(CsvReader.Read(ContactCsvExporter.Write(book)));
        Assert.True(mapped.IsSuccess);

        var outcome = await new ContactStore(new PreferencesTestDbContext(db)).ImportAsync(
            user,
            [.. mapped.Value.Select(r => new ContactImportRow(
                r.Line, r.FirstName, r.LastName, r.Nickname, r.IsFavorite, r.Addresses, null))],
            CancellationToken.None);

        Assert.Equal(0, outcome.Created);
        Assert.Equal(3, outcome.Merged);
        var after = await new ContactStore(new PreferencesTestDbContext(db)).ListAsync(user, CancellationToken.None);
        Assert.Equal(3, after.Count);
        var bruno = after.Single(c => c.FirstName == "Bruno");
        Assert.Equal("bruno", bruno.Nickname);
        Assert.True(bruno.IsFavorite);
        Assert.Equal(["bruno@example.com", "second@example.com"], bruno.Addresses);
        Assert.Contains(after, c => c.Nickname == "zorro");
    }

    // A name a spreadsheet would evaluate goes out behind an apostrophe, and comes back as itself.
    [Fact]
    public void Write_NeutralisesAFormulaNameAndTheImportUndoesIt()
    {
        var document = Parse(ContactCsvExporter.Write(
            [Contact(first: "=1+1", last: "@SUM", addresses: "a@example.com")]));

        Assert.Equal(["'=1+1", "'@SUM"], Assert.Single(document.Rows).Fields.Take(2));
        var row = Assert.Single(ContactCsvMapper.Map(document).Value);
        Assert.Equal("=1+1", row.FirstName);
        Assert.Equal("@SUM", row.LastName);
    }

    // The apostrophe is a trigger itself, or a name legitimately starting with one would be written
    // bare and read back one character shorter.
    [Fact]
    public void Write_KeepsANameThatReallyStartsWithAnApostrophe()
    {
        var document = Parse(ContactCsvExporter.Write([Contact(first: "'Tonio", addresses: "a@example.com")]));

        Assert.Equal("''Tonio", Assert.Single(document.Rows).Fields[0]);
        Assert.Equal("'Tonio", Assert.Single(ContactCsvMapper.Map(document).Value).FirstName);
    }
}
