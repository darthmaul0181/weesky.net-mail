using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Configuration;

public sealed class DavOptionsTests
{
    /// <summary>
    /// The eager pass a real start runs, and nothing else: reading <c>IOptions.Value</c> would
    /// validate lazily, so dropping <c>ValidateOnStart</c> would leave the refusals below green
    /// while the service booted on a bad address and failed on the first request instead. The
    /// signing key is set because <c>AddSnoopyOptions</c> validates it on start too, and an unset
    /// one would make every case here throw for the wrong reason.
    /// </summary>
    private static IServiceProvider Start(string? publicUrl)
    {
        var values = new Dictionary<string, string?>
        {
            ["TokenConstants:Key"] = new string('k', 32)
        };
        if (publicUrl is not null) values["Dav:PublicUrl"] = publicUrl;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var provider = new ServiceCollection().AddSnoopyOptions(configuration).BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();

        return provider;
    }

    private static DavOptions Build(string? publicUrl) =>
        Start(publicUrl).GetRequiredService<IOptions<DavOptions>>().Value;

    [Theory]
    [InlineData("https://api.mail.weesky.net")]
    // An internationalised host, its punycode spelling, and the two literal forms: all four are
    // origins a proxy really publishes, and the authority comparison must not cost them.
    [InlineData("https://api.mäil.example")]
    [InlineData("https://xn--mil-6ka.example")]
    [InlineData("https://192.0.2.10")]
    [InlineData("https://[2001:db8::1]")]
    // Scheme and host are case-insensitive by RFC 3986 and Uri lowers both, so case is a spelling
    // an operator may write and not a form to refuse.
    [InlineData("https://API.mail.weesky.net")]
    [InlineData("HTTPS://API.MAIL.WEESKY.NET")]
    public void ABareHttpsOrigin_IsAccepted(string publicUrl)
    {
        var options = Build(publicUrl);

        Assert.True(options.IsConfigured);
        Assert.Equal(publicUrl, options.PublicUrl);
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
    // Userinfo: the screen publishes this string verbatim into a client that would then send the
    // sync secret with a username and password nobody chose.
    [InlineData("https://user:pass@api.mail.weesky.net")]
    // The one this validator exists for: it reads to a human as our host, and it authenticates to
    // evil.com. Uri resolves the authority past the "@"; the text alone does not. In mixed case
    // too — the comparison ignores case, which must cost it none of its reach.
    [InlineData("https://api.mail.weesky.net@evil.com")]
    [InlineData("https://API.mail.weesky.net@Evil.com")]
    // A fragment is not part of an origin, and a client concatenating a path onto it builds a URL
    // whose path lands after the "#" — a request to the bare host.
    [InlineData("https://api.mail.weesky.net#frag")]
    [InlineData("https://api.mail.weesky.net?q=1")]
    public void AnythingElse_RefusesToStart(string publicUrl)
    {
        var exception = Assert.Throws<OptionsValidationException>(() => Start(publicUrl));

        Assert.Contains("Dav:PublicUrl", exception.Message, StringComparison.Ordinal);
    }
}
