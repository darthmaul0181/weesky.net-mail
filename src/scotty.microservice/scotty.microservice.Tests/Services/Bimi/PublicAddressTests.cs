using System.Net;
using weesky.Scotty.Microservice.Services.Bimi;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services.Bimi;

public sealed class PublicAddressTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::127.0.0.1")]
    [InlineData("::10.0.0.1")]
    [InlineData("::169.254.169.254")]
    [InlineData("2002:7f00:1::1")]
    [InlineData("2002:0a00:0001::1")]
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2")]
    public void A_non_public_address_is_refused(string address) =>
        Assert.False(PublicAddress.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("2606:2800:220:1::1")]
    [InlineData("2002:5db8:d822::1")]
    public void A_public_address_is_accepted(string address) =>
        Assert.True(PublicAddress.IsPublic(IPAddress.Parse(address)));
}
