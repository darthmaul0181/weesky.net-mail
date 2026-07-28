using System.Text;
using weesky.Snoopy.Microservice.Services.Csv;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class CsvWriterTests
{
    private static string Text(byte[] content) =>
        new UTF8Encoding(false).GetString(content, 3, content.Length - 3);

    [Fact]
    public void Write_EmitsHeaderThenRows()
    {
        var content = CsvWriter.Write(["A", "B"], [["x", "y"]]);

        Assert.Equal("A,B\r\nx,y\r\n", Text(content));
    }

    // Without it Excel reads a UTF-8 export in 1252 and renders "Dupré" as "DuprÃ©".
    [Fact]
    public void Write_StartsWithTheByteOrderMark()
    {
        var content = CsvWriter.Write(["A"], []);

        Assert.Equal(Encoding.UTF8.GetPreamble(), content[..3]);
    }

    [Fact]
    public void Write_QuotesOnlyWhatNeedsIt()
    {
        var content = CsvWriter.Write(["A", "B", "C"], [["plain", "has,comma", "has\nnewline"]]);

        Assert.Equal("A,B,C\r\nplain,\"has,comma\",\"has\nnewline\"\r\n", Text(content));
    }

    [Fact]
    public void Write_DoublesAnEmbeddedQuote()
    {
        var content = CsvWriter.Write(["A"], [["say \"hi\""]]);

        Assert.Equal("A\r\n\"say \"\"hi\"\"\"\r\n", Text(content));
    }

    [Fact]
    public void Write_RoundTripsThroughTheReader()
    {
        var content = CsvWriter.Write(["A", "B"], [["Mertens, Bruno", "say \"hi\""]]);

        var row = Assert.Single(CsvReader.Read(content).Rows);
        Assert.Equal(["Mertens, Bruno", "say \"hi\""], row.Fields);
    }
}
