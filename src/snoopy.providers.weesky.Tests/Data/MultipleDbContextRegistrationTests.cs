using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using weesky.Snoopy.Providers.Weesky.Data;
using weesky.Snoopy.Microservice.Data.Preferences;
using Xunit;

namespace weesky.Snoopy.Providers.Weesky.Tests.Data;

// The two contexts live in different assemblies now; they still share one container, and the
// dovecot one is the second registration. Every other test builds its context by hand, so the suite stayed green while the running
// service failed on the first request that touched the database. These go through the
// container, the way Program.cs does.
public sealed class MultipleDbContextRegistrationTests
{
    private static ServiceProvider BuildProviderWithBothContexts()
    {
        var services = new ServiceCollection();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase("accounts"));
        services.AddDbContext<PreferencesDbContext>(options =>
            options.UseInMemoryDatabase("preferences"));

        return services.BuildServiceProvider();
    }

    [Fact]
    public void ApplicationDbContext_ResolvesWhenASecondContextIsRegistered()
    {
        using var provider = BuildProviderWithBothContexts();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    [Fact]
    public void PreferencesDbContext_ResolvesWhenASecondContextIsRegistered()
    {
        using var provider = BuildProviderWithBothContexts();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<PreferencesDbContext>());
    }
}
