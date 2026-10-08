using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using weesky.Scotty.Microservice.Configuration;
using weesky.Scotty.Microservice.Models;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Models;

public sealed class ProductVersionTests
{
    [Fact]
    public void Parse_SplitsTheVersionFromTheCommitTheSdkAppends()
        => Assert.Equal(new ProductVersion("1.1.0-dev", "0a1b2c3"),
            ProductVersion.Parse("1.1.0-dev+0a1b2c3d4e5f60718293a4b5c6d7e8f901234567"));

    [Fact]
    public void Parse_LeavesTheCommitNullWhenTheBuildCarriedNone()
        => Assert.Equal(new ProductVersion("1.0.0", null), ProductVersion.Parse("1.0.0"));

    [Fact]
    public void Parse_KeepsTheImageNumberTheContainerCarries()
        => Assert.Equal(new ProductVersion("1.4.0", "0a1b2c3", "1.0.0"),
            ProductVersion.Parse("1.4.0+0a1b2c3d4e5f", " 1.0.0 "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Parse_LeavesTheImageNullOutsideAContainer(string? image)
        => Assert.Null(ProductVersion.Parse("1.4.0", image).Image);

    [Fact]
    public void Json_OmitsTheImageOutsideAContainer()
    {
        var json = new JsonOptions();
        MvcFormatterConfiguration.ConfigureJson(json);

        Assert.Equal("{\"version\":\"1.4.0\",\"commit\":\"0a1b2c3\"}",
            JsonSerializer.Serialize(new ProductVersion("1.4.0", "0a1b2c3"), json.JsonSerializerOptions));
        Assert.Contains("\"image\":\"1.0.0\"",
            JsonSerializer.Serialize(new ProductVersion("1.4.0", "0a1b2c3", "1.0.0"), json.JsonSerializerOptions));
    }

    [Fact]
    public void Current_IsReadFromTheMicroserviceVersionFile()
    {
        var expected = File.ReadAllText(FindVersionFile()).Trim();

        Assert.StartsWith(expected, ProductVersion.Current.Version);
    }

    // A test build is not a release build, so the -dev suffix is what it must carry.
    [Fact]
    public void Current_IsADevelopmentVersionOutsideARelease()
        => Assert.EndsWith("-dev", ProductVersion.Current.Version);

    private static string FindVersionFile()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "scotty.microservice", "VERSION");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("No src/scotty.microservice/VERSION above the test binaries");
    }
}
