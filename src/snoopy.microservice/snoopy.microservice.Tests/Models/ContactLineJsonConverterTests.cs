using System.Text.Json;
using weesky.Snoopy.Microservice.Models.Contacts;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

public sealed class ContactLineJsonConverterTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static ContactRequest Deserialize(string json) =>
        JsonSerializer.Deserialize<ContactRequest>(json, Web)!;

    [Fact]
    public void Read_ABareStringBecomesAnAddressWithNoPositionOrType()
    {
        var request = Deserialize("""{"addresses":["a@b.c"]}""");

        var line = Assert.Single(request.Addresses!);
        Assert.Null(line.Position);
        Assert.Equal("a@b.c", line.Address);
        Assert.Null(line.Type);
    }

    [Fact]
    public void Read_AnObjectCarriesItsPositionAndType()
    {
        var request = Deserialize("""{"addresses":[{"position":0,"address":"a@b.c","type":"HOME"}]}""");

        var line = Assert.Single(request.Addresses!);
        Assert.Equal(0, line.Position);
        Assert.Equal("a@b.c", line.Address);
        Assert.Equal("HOME", line.Type);
    }

    [Fact]
    public void Read_MixesStringsAndObjectsInOneArray()
    {
        var request = Deserialize(
            """{"addresses":["a@b.c",{"position":1,"address":"d@e.f","type":"WORK"}]}""");

        Assert.Equal(2, request.Addresses!.Count);
        Assert.Equal("a@b.c", request.Addresses[0].Address);
        Assert.Equal("d@e.f", request.Addresses[1].Address);
    }

    [Fact]
    public void Read_AnAbsentPropertyStaysNull()
    {
        Assert.Null(Deserialize("{}").Addresses);
    }

    [Fact]
    public void Read_ANullPropertyStaysNull()
    {
        Assert.Null(Deserialize("""{"addresses":null}""").Addresses);
    }

    [Theory]
    [InlineData("""{"addresses":[42]}""")]
    [InlineData("""{"addresses":[true]}""")]
    [InlineData("""{"addresses":[null]}""")]
    [InlineData("""{"addresses":[["nested"]]}""")]
    [InlineData("""{"addresses":{"address":"a@b.c"}}""")]
    public void Read_RefusesAnyOtherToken(string json)
    {
        Assert.Throws<JsonException>(() => Deserialize(json));
    }
}
