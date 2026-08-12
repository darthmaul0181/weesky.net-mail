using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Providers.Weesky.Authorization;
using weesky.Snoopy.Providers.Weesky.Models;
using weesky.Snoopy.Providers.Weesky.Platform;
using weesky.Snoopy.Providers.Weesky.Repositories;
using Xunit;

namespace weesky.Snoopy.Providers.Weesky.Tests.Configuration;

/// <summary>
/// What the platform puts in the container. Everything here is only ever reached by constructor
/// injection, so a missing registration builds and ships and fails on the first live request.
/// </summary>
public sealed class WeeskyPlatformTests
{
    [Theory]
    [InlineData(typeof(IAliasDirectory), typeof(WeeskyAliasDirectory))]
    [InlineData(typeof(IProfileReader), typeof(WeeskyProfileReader))]
    [InlineData(typeof(IAccountInfoProvider), typeof(WeeskyAccountInfoProvider))]
    [InlineData(typeof(IUsersRepository), typeof(UsersRepository))]
    [InlineData(typeof(IAliasesRepository), typeof(AliasesRepository))]
    [InlineData(typeof(IAdminRepository), typeof(AdminRepository))]
    public void AddWeeskyPlatformServices_RegistersTheWeeskyImplementationScoped(Type port, Type implementation)
    {
        var services = new ServiceCollection().AddWeeskyPlatformServices();

        var descriptor = Assert.Single(services, d => d.ServiceType == port);
        Assert.Equal(implementation, descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    /// <summary>The core declares the Admin policy and registers no handler; this is the one that
    /// can satisfy it, which is why the policy is unsatisfiable on any other platform.</summary>
    [Fact]
    public void AddWeeskyPlatformServices_BringsTheAdminAuthorizationHandler()
    {
        var services = new ServiceCollection().AddWeeskyPlatformServices();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IAuthorizationHandler));
        Assert.Equal(typeof(AdminRequirementHandler), descriptor.ImplementationType);
    }

    [Fact]
    public void AddWeeskyPlatform_WithoutTheConnectionString_RefusesToStartNamingTheKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Weesky:Dovecot:ApiUrl"] = "http://localhost" })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddWeeskyPlatform(configuration));

        Assert.Equal("Weesky:ConnectionStrings:MailUserAccountsDatabase", WeeskyPlatform.ConnectionStringKey);
        Assert.Contains(WeeskyPlatform.ConnectionStringKey, error.Message);
        Assert.Contains("generic", error.Message);
    }

    /// <summary>Both halves of the doveadm block are required, and a deployment that dropped one
    /// must learn it at startup — not from the admin quota column silently erroring later.</summary>
    [Theory]
    [InlineData(null, "secret", WeeskyPlatform.DovecotApiUrlKey)]
    [InlineData("", "secret", WeeskyPlatform.DovecotApiUrlKey)]
    [InlineData("mail.example.net/doveadm/v1", "secret", WeeskyPlatform.DovecotApiUrlKey)]
    [InlineData("http://localhost/doveadm/v1", null, WeeskyPlatform.DovecotApiKeyKey)]
    [InlineData("http://localhost/doveadm/v1", "   ", WeeskyPlatform.DovecotApiKeyKey)]
    public void Dovecot_options_missing_a_half_refuse_to_start_naming_the_key(string? url, string? key, string named)
    {
        using var provider = ComposeDovecot(url, key);

        var error = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dovecot_options_complete_pass_validation_and_bind()
    {
        using var provider = ComposeDovecot("http://localhost:8080/doveadm/v1", "secret");

        provider.GetRequiredService<IStartupValidator>().Validate();

        var options = provider.GetRequiredService<IOptions<DovecotOptions>>().Value;
        Assert.Equal("http://localhost:8080/doveadm/v1", options.ApiUrl);
        Assert.Equal("secret", options.ApiKey);
    }

    /// <summary>And it is the <em>host start</em> that runs it, not a call the service code makes:
    /// the whole point is that the operator sees the refusal instead of a user seeing a 500.</summary>
    [Fact]
    public async Task A_host_missing_the_doveadm_key_refuses_to_start()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(new());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{WeeskyPlatform.SectionName}:Dovecot:ApiUrl"] = "http://localhost/doveadm/v1"
        });
        builder.Services.AddDovecotOptions(builder.Configuration.GetSection(WeeskyPlatform.SectionName));

        using var host = builder.Build();

        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(CancellationToken.None));
        Assert.Contains(WeeskyPlatform.DovecotApiKeyKey, error.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider ComposeDovecot(string? url, string? key)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{WeeskyPlatform.SectionName}:Dovecot:ApiUrl"] = url,
                [$"{WeeskyPlatform.SectionName}:Dovecot:ApiKey"] = key
            })
            .Build();

        return new ServiceCollection()
            .AddDovecotOptions(configuration.GetSection(WeeskyPlatform.SectionName))
            .BuildServiceProvider();
    }

    /// <summary>The root-level key is what the pre-split deployment carries; reading it here would
    /// let a half-migrated configuration start and then fail on the first query.</summary>
    [Fact]
    public void AddWeeskyPlatform_IgnoresTheOldRootLevelConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MailUserAccountsDatabase"] = "Server=localhost;Database=dovecot"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddWeeskyPlatform(configuration));
    }
}
