using weesky.Scotty.Microservice.Models;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Models;

public sealed class BimiRequestTests
{
    [Theory]
    [InlineData("paypal.com")]
    [InlineData("PayPal.COM")]
    [InlineData("paypal.com.")]
    [InlineData("news.example.co.uk")]
    public void Valid_domains_are_accepted(string domain) => Assert.True(new BimiRequest(domain, null).IsValid);

    [Theory]
    [InlineData("a@b.c")]
    [InlineData("localhost")]
    [InlineData("1.2.3.4")]
    [InlineData("a..b")]
    [InlineData("-a.com")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_domains_are_rejected(string? domain) => Assert.False(new BimiRequest(domain, null).IsValid);

    [Fact]
    public void A_254_character_domain_is_rejected() =>
        Assert.False(new BimiRequest(new string('a', 250) + ".com", null).IsValid);

    [Fact]
    public void The_domain_is_trimmed_lower_cased_and_loses_its_trailing_dot() =>
        Assert.Equal("paypal.com", new BimiRequest(" PayPal.COM. ", null).DomainName);

    [Theory]
    [InlineData(null, "default")]
    [InlineData("", "default")]
    [InlineData("Brand", "brand")]
    [InlineData("my.sel_1-x", "my.sel_1-x")]
    public void The_selector_is_normalised(string? selector, string expected) =>
        Assert.Equal(expected, new BimiRequest("paypal.com", selector).SelectorName);

    [Theory]
    [InlineData("Bad Sel")]
    [InlineData(".dot")]
    [InlineData("-dash")]
    [InlineData("a/b")]
    public void A_bad_selector_is_rejected(string selector) =>
        Assert.False(new BimiRequest("paypal.com", selector).IsValid);

    [Fact]
    public void A_64_character_selector_is_rejected() =>
        Assert.False(new BimiRequest("paypal.com", new string('a', 64)).IsValid);

    [Fact]
    public void A_63_character_selector_is_accepted() =>
        Assert.True(new BimiRequest("paypal.com", new string('a', 63)).IsValid);
}
