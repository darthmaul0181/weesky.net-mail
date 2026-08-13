using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Platform.Generic;
using weesky.Snoopy.Providers.Weesky;
using weesky.Snoopy.Providers.Weesky.Data;
using weesky.Snoopy.Providers.Weesky.Platform;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Configuration;

/// <summary>
/// Both platforms answer every port the running service injects, and each answers with its own
/// adapter. Nothing else proves it: a port left unregistered builds, ships, and fails on the first
/// request that needs it.
///
/// This composes the container the way <c>Program.cs</c> does rather than booting the host. Two
/// pieces of the real startup need machines this suite does not have — <c>ServerVersion.AutoDetect</c>
/// opens a MySQL connection for each database, and <c>UseSnoopyLogging</c> creates
/// <c>/var/log/snoopy.microservice</c>, which a CI runner may not write to. Both contexts are
/// therefore registered in memory here; everything between them is the real registration code.
/// </summary>
public sealed class PlatformBootTests
{
    private static ServiceProvider Compose(string platform)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Platform"] = platform,
                ["TokenConstants:Key"] = new string('k', 64),
                ["TokenConstants:Issuer"] = "test",
                ["TokenConstants:Audience"] = "test",
                [$"{WeeskyPlatform.SectionName}:Dovecot:ApiUrl"] = "http://localhost/doveadm/v1"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PreferencesDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        services
            .AddSnoopyOptions(configuration)
            .AddMailServices()
            .AddRuleProviders()
            .AddRepositories()
            .AddSnoopyAuthentication();

        if (configuration.UsesWeeskyPlatform())
        {
            services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddWeeskyPlatformServices();
        }
        else
        {
            services.AddGenericPlatform();
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    [Theory]
    [InlineData(PlatformOptions.Weesky, typeof(WeeskyAliasDirectory), typeof(WeeskyProfileReader), typeof(WeeskyAccountInfoProvider))]
    [InlineData(PlatformOptions.Generic, typeof(FreeIdentityDirectory), typeof(NullProfileReader), typeof(ClaimsAccountInfoProvider))]
    public void Host_boots_and_resolves_every_port(string platform, Type aliases, Type profile, Type account)
    {
        using var provider = Compose(platform);
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        Assert.IsType(aliases, services.GetRequiredService<IAliasDirectory>());
        Assert.IsType(profile, services.GetRequiredService<IProfileReader>());
        Assert.IsType(account, services.GetRequiredService<IAccountInfoProvider>());
        Assert.NotNull(services.GetRequiredService<IUserAuthenticator>());
        Assert.NotNull(services.GetRequiredService<ISessionGuard>());
    }

    /// <summary>The three ports move together: a deployment mixing them would enforce ownership
    /// against an alias table it has no reason to trust, or stop enforcing it while holding one.</summary>
    [Theory]
    [InlineData(PlatformOptions.Weesky, true)]
    [InlineData(PlatformOptions.Generic, false)]
    public void Ownership_enforcement_follows_the_platform(string platform, bool enforces)
    {
        using var provider = Compose(platform);
        using var scope = provider.CreateScope();

        Assert.Equal(enforces, scope.ServiceProvider.GetRequiredService<IAliasDirectory>().EnforcesOwnership);
    }

    [Fact]
    public void PlatformOptions_is_bound_from_the_root_key()
    {
        using var provider = Compose(PlatformOptions.Weesky);

        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlatformOptions>>().Value;

        Assert.Equal(PlatformOptions.Weesky, options.Platform);
        Assert.True(options.IsWeesky);
    }

    [Fact]
    public void Weesky_platform_without_weesky_block_refuses_to_start_naming_the_key()
    {
        var configuration = new ConfigurationBuilder().Build();

        var error = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddWeeskyPlatform(configuration));

        Assert.Contains(WeeskyPlatform.ConnectionStringKey, error.Message);
    }
}
