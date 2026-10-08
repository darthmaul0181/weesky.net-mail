using Microsoft.Extensions.Configuration;
using weesky.Scotty.Microservice.Configuration;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

public sealed class FrontendSettingsTests : IDisposable
{
    private readonly DirectoryInfo dist = Directory.CreateTempSubdirectory("scotty-dist-");

    public FrontendSettingsTests() => File.WriteAllText(Path.Combine(dist.FullName, "index.html"), "<!doctype html>");

    public void Dispose() => dist.Delete(recursive: true);

    private static FrontendSettings? Read(string? path, string? apiBase = null) =>
        FrontendSettings.Read(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:Path"] = path,
                ["Frontend:ApiBase"] = apiBase,
            })
            .Build());

    [Fact]
    public void WithoutAPath_ThereIsNoFrontend() => Assert.Null(Read(null, "https://api.example.net"));

    [Fact]
    public void APathWithoutIndex_RefusesToStart()
    {
        var empty = Directory.CreateTempSubdirectory("scotty-empty-");
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => Read(empty.FullName));
            Assert.Contains(empty.FullName, error.Message);
        }
        finally
        {
            empty.Delete();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyApiBase_MeansOneAddress(string? apiBase)
    {
        var settings = Read(dist.FullName, apiBase)!;

        Assert.Equal(dist.FullName, settings.Root);
        Assert.Equal("", settings.ApiBase);
        Assert.Null(settings.ApiHost);
    }

    [Theory]
    [InlineData("https://api.example.net", "https://api.example.net", "api.example.net")]
    [InlineData("https://API.Example.net/", "https://api.example.net", "api.example.net")]
    [InlineData("http://api.example.net:8443", "http://api.example.net:8443", "api.example.net")]
    public void AnOrigin_IsKeptWithoutItsTrailingSlash(string apiBase, string expected, string host)
    {
        var settings = Read(dist.FullName, apiBase)!;

        Assert.Equal(expected, settings.ApiBase);
        Assert.Equal(host, settings.ApiHost);
    }

    [Fact]
    public void APath_IsResolvedOnce()
    {
        var roundabout = Path.Combine(dist.FullName, "..", dist.Name);

        Assert.Equal(dist.FullName, Read(roundabout)!.Root);
    }

    [Fact]
    public void AnInternationalHost_IsComparedInTheFormBrowsersSend()
        => Assert.Equal("xn--bcher-kva.example", Read(dist.FullName, "https://bücher.example")!.ApiHost);

    [Theory]
    [InlineData("api.example.net")]
    [InlineData("ftp://api.example.net")]
    [InlineData("https://example.net/scotty")]
    [InlineData("https://example.net/?x=1")]
    [InlineData("https://user:secret@api.example.net")]
    public void AnythingElse_RefusesToStartNamingTheValue(string apiBase)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Read(dist.FullName, apiBase));

        Assert.Contains($"'{apiBase}'", error.Message);
    }
}
