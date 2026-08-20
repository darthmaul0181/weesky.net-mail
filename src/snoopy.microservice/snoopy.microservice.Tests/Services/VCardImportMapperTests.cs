using weesky.Snoopy.Microservice.Services.Contacts;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class VCardImportMapperTests
{
    private static string Card(params string[] lines) =>
        "BEGIN:VCARD\r\nVERSION:3.0\r\n" + string.Concat(lines.Select(l => l + "\r\n")) + "END:VCARD\r\n";

    private static VCardChunk Chunk(string card, int line = 4) => new(line, card);

    [Fact]
    public void Map_ReadsTheNamesTheUidAndTheAddresses()
    {
        var row = VCardImportMapper.Map(Chunk(Card(
            "N:Mertens;Bruno;;;", "FN:Bruno Mertens", "NICKNAME:bruno", "UID:card-42",
            "EMAIL;TYPE=INTERNET:bruno@example.com", "EMAIL:second@example.com")));

        Assert.Equal(4, row.Line);
        Assert.Equal("Bruno", row.FirstName);
        Assert.Equal("Mertens", row.LastName);
        Assert.Equal("bruno", row.Nickname);
        Assert.Equal("card-42", row.Uid);
        Assert.Equal(["bruno@example.com", "second@example.com"], row.Addresses);
    }

    // Décision 1's key assertion: what the store files is the chunk's own bytes, never a
    // re-serialisation — a card that came back different would burn its ETag on arrival.
    [Fact]
    public void Map_CarriesTheCardVerbatim()
    {
        var chunk = Chunk(Card("FN:Ana", "X-WHATEVER;X-P=1:kept"));

        Assert.Equal(chunk.Text, VCardImportMapper.Map(chunk).VCard);
    }

    [Fact]
    public void Map_DropsAnAddressTheBookCouldNotUse()
    {
        var row = VCardImportMapper.Map(Chunk(Card("FN:Ana", "EMAIL:not-an-address")));

        Assert.Empty(row.Addresses);
    }

    // The one thing the projection cannot answer: it mirrors the column's 255 characters, and a
    // truncated UID is a synchronisation identity the card does not carry.
    [Fact]
    public void Map_AnswersTheUidUntruncated()
    {
        var uid = new string('u', 300);

        Assert.Equal(uid, VCardImportMapper.Map(Chunk(Card("FN:Ana", $"UID:{uid}"))).Uid);
    }

    [Fact]
    public void Map_FallsBackToTheDisplayNameWhenTheCardNamesNothingElse()
    {
        var row = VCardImportMapper.Map(Chunk(Card("FN:Ana Solo")));

        Assert.Null(row.FirstName);
        Assert.Equal("Ana Solo", row.Nickname);
    }

    [Fact]
    public void Map_AnswersAnEmptyRowForAnUnreadableCard()
    {
        var row = VCardImportMapper.Map(Chunk("BEGIN:VCARD\r\n"));

        Assert.Null(row.Uid);
        Assert.Empty(row.Addresses);
        Assert.False(row.IsFavorite);
    }
}
