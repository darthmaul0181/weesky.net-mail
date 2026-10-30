using weesky.Scotty.Microservice.Services.Bimi;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services.Bimi;

public sealed class BimiRecordTests
{
    [Theory]
    [InlineData("v=BIMI1; l=https://x.example/l.svg; a=https://x.example/vmc.pem")]
    [InlineData("  v=BIMI1 ;  l = https://x.example/l.svg ;")]
    public void A_well_formed_record_gives_the_logo(string record) =>
        Assert.Equal(new Uri("https://x.example/l.svg"), BimiRecord.LogoOf([record]));

    [Fact]
    public void The_first_BIMI_record_wins() =>
        Assert.Equal(new Uri("https://a.example/1.svg"),
            BimiRecord.LogoOf(["v=spf1 -all", "v=BIMI1; l=https://a.example/1.svg", "v=BIMI1; l=https://b.example/2.svg"]));

    [Theory]
    [InlineData("v=BIMI1; l=http://x.example/l.svg")]
    [InlineData("v=BIMI1; l=https://x.example:8443/l.svg")]
    [InlineData("v=BIMI1; l=https://10.0.0.1/l.svg")]
    [InlineData("v=BIMI1; l=https://[::1]/l.svg")]
    [InlineData("v=BIMI1; l=https://u:p@x.example/l.svg")]
    [InlineData("v=BIMI1; l=")]
    [InlineData("v=BIMI1; a=https://x.example/vmc.pem")]
    [InlineData("l=https://x.example/l.svg")]
    [InlineData("v=BIMI1; l=https://x.example/a.svg; l=https://x.example/b.svg")]
    [InlineData("v=BIMI1; l=not a url")]
    public void An_unusable_record_gives_nothing(string record) => Assert.Null(BimiRecord.LogoOf([record]));

    [Fact]
    public void No_records_give_nothing() => Assert.Null(BimiRecord.LogoOf([]));
}
