using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

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
    public void Current_IsReadFromTheVersionFileAtTheRepositoryRoot()
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
            var candidate = Path.Combine(dir.FullName, "VERSION");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("No VERSION file above the test binaries");
    }
}
