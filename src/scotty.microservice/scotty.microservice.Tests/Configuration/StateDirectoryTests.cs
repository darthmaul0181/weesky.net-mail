using Microsoft.Extensions.Hosting;
using Moq;
using weesky.Scotty.Microservice.Configuration;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

public sealed class StateDirectoryTests
{
    private static IHostEnvironment Environment(string name)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(name);
        environment.SetupGet(e => e.ContentRootPath).Returns(Path.Combine("srv", "app"));
        return environment.Object;
    }

    [Fact]
    public void Resolve_TakesTheFirstDirectorySystemdPasses()
    {
        Assert.Equal("/var/lib/scotty.microservice",
            StateDirectory.Resolve(Environment(Environments.Production), "/var/lib/scotty.microservice:/var/lib/other"));
    }

    [Fact]
    public void Resolve_FallsBackToTheIgnoredKeysFolderInDevelopment()
    {
        Assert.Equal(Path.Combine("srv", "app", "keys"),
            StateDirectory.Resolve(Environment(Environments.Development), null));
    }

    [Fact]
    public void Resolve_RefusesToStartOutsideDevelopmentWithoutStateDirectory()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => StateDirectory.Resolve(Environment(Environments.Production), ""));

        Assert.Contains("STATE_DIRECTORY is not set", error.Message);
    }
}
