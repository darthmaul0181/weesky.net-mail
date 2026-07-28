using System.Text;
using weesky.Snoopy.Microservice.Services.Csv;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class CsvReaderTests
{
    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    [Fact]
    public void Read_SplitsHeaderAndRows()
    {
        var document = CsvReader.Read(Utf8("First Name,Last Name\r\nBruno,Mertens\r\n"));

        Assert.Equal(["First Name", "Last Name"], document.Header);
        var row = Assert.Single(document.Rows);
        Assert.Equal(["Bruno", "Mertens"], row.Fields);
    }

    // The number the user reads in their spreadsheet, header included — not the data index.
    [Fact]
    public void Read_NumbersRowsFromTwo()
    {
        var document = CsvReader.Read(Utf8("A,B\r\nx,y\r\nz,w\r\n"));

        Assert.Equal([2, 3], document.Rows.Select(r => r.Line));
    }

    [Fact]
    public void Read_KeepsDelimiterInsideQuotes()
    {
        var document = CsvReader.Read(Utf8("A,B\r\n\"Mertens, Bruno\",x\r\n"));

        Assert.Equal("Mertens, Bruno", Assert.Single(document.Rows).Fields[0]);
    }

    [Fact]
    public void Read_KeepsNewlineInsideQuotes_AndCountsIt()
    {
        var document = CsvReader.Read(Utf8("A,B\r\n\"one\ntwo\",x\r\nlast,y\r\n"));

        Assert.Equal("one\ntwo", document.Rows[0].Fields[0]);
        Assert.Equal(4, document.Rows[1].Line);
    }

    [Fact]
    public void Read_UnescapesDoubledQuote()
    {
        var document = CsvReader.Read(Utf8("A\r\n\"say \"\"hi\"\"\"\r\n"));

        Assert.Equal("say \"hi\"", Assert.Single(document.Rows).Fields[0]);
    }

    // Excel in a French locale writes semicolons; read with a comma the file is one column, which
    // is not an error but an import that silently does nothing.
    [Theory]
    [InlineData(';')]
    [InlineData('\t')]
    public void Read_SniffsTheDelimiter(char delimiter)
    {
        var document = CsvReader.Read(Utf8($"First Name{delimiter}Last Name\r\nBruno{delimiter}Mertens\r\n"));

        Assert.Equal(["First Name", "Last Name"], document.Header);
        Assert.Equal(["Bruno", "Mertens"], Assert.Single(document.Rows).Fields);
    }

    [Fact]
    public void Read_StripsTheByteOrderMark()
    {
        var content = (byte[])[.. Encoding.UTF8.GetPreamble(), .. Utf8("First Name\r\nBruno\r\n")];

        Assert.Equal("First Name", Assert.Single(CsvReader.Read(content).Header));
    }

    // Outlook still exports Windows-1252, which Latin-1 matches on every accented letter a name
    // can carry.
    [Fact]
    public void Read_FallsBackToLatin1WhenNotUtf8()
    {
        var content = Encoding.Latin1.GetBytes("Name\r\nDupré\r\n");

        Assert.Equal("Dupré", Assert.Single(CsvReader.Read(content).Rows).Fields[0]);
    }

    [Fact]
    public void Read_DropsBlankLines()
    {
        var document = CsvReader.Read(Utf8("A,B\r\nx,y\r\n,\r\n\r\n"));

        Assert.Single(document.Rows);
    }

    [Fact]
    public void Read_AnswersEmptyForAnEmptyFile()
    {
        var document = CsvReader.Read([]);

        Assert.Empty(document.Header);
        Assert.Empty(document.Rows);
    }
}
