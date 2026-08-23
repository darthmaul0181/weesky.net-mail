using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Configuration;

public sealed class DavOptionsTests
{
    private static DavOptions Build(string? publicUrl)
    {
        var values = new Dictionary<string, string?>();
        if (publicUrl is not null) values["Dav:PublicUrl"] = publicUrl;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var provider = new ServiceCollection().AddSnoopyOptions(configuration).BuildServiceProvider();

        return provider.GetRequiredService<IOptions<DavOptions>>().Value;
    }

    [Fact]
    public void ABareHttpsOrigin_IsAccepted()
    {
        var options = Build("https://api.mail.weesky.net");

        Assert.True(options.IsConfigured);
        Assert.Equal("https://api.mail.weesky.net", options.PublicUrl);
    }

    [Fact]
    public void NullValue_IsLegalAndMeansTheFeatureIsOff()
    {
        // A deployment that serves no /dav must not be forced to invent an address.
        Assert.False(Build(null).IsConfigured);
    }

    [Fact]
    public void EmptyValue_IsLegalAndMeansTheFeatureIsOff()
    {
        Assert.False(Build("").IsConfigured);
    }

    [Theory]
    // A path would break the clients that concatenate /.well-known/carddav onto it.
    [InlineData("https://api.mail.weesky.net/dav")]
    [InlineData("https://api.mail.weesky.net/")]
    // A port is ignored by some iOS versions, which try 443 then 80 whatever they were given.
    [InlineData("https://api.mail.weesky.net:8443")]
    // An explicit default port is still a port an operator wrote — Uri normalises it away, but the
    // client on the wire got told to include one.
    [InlineData("https://api.mail.weesky.net:443")]
    // Basic carries the secret in clear; an http address published here invites exactly that.
    [InlineData("http://api.mail.weesky.net")]
    [InlineData("api.mail.weesky.net")]
    // Whitespace makes the stored value differ from the one that was validated.
    [InlineData(" https://api.mail.weesky.net")]
    [InlineData("https://api.mail.weesky.net ")]
    public void AnythingElse_RefusesToStart(string publicUrl)
    {
        var exception = Assert.Throws<OptionsValidationException>(() => Build(publicUrl));

        Assert.Contains("Dav:PublicUrl", exception.Message, StringComparison.Ordinal);
    }
}
