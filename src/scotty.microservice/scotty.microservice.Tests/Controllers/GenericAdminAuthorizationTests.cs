using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Scotty.Microservice.Configuration;
using weesky.Scotty.Microservice.Controllers;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Platform.Generic;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Controllers;

/// <summary>
/// A generic deployment routed for real: the admin policy is answered by the handler
/// AddGenericPlatform registers from Generic:Administrators, not by a stand-in.
/// </summary>
public sealed class GenericAdminAuthorizationTests : IAsyncLifetime
{
    private const string UserHeader = "X-Test-User";

    private readonly string _database = Guid.NewGuid().ToString("N");
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new(GenericAdministrators.ConfigurationKey, "michael@exemple.be")])
            .Build();

        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddControllers()
                        .AddJsonOptions(MvcFormatterConfiguration.ConfigureJson)
                        .ConfigureApplicationPartManager(manager =>
                        {
                            manager.ApplicationParts.Clear();
                            manager.ApplicationParts.Add(new AssemblyPart(typeof(ExternalDomainsController).Assembly));
                            manager.FeatureProviders.Clear();
                            manager.FeatureProviders.Add(new OnlyController());
                        });
                    services.AddScottyAuthentication()
                        .AddAuthentication()
                        .AddScheme<AuthenticationSchemeOptions, UserHandler>(UserHandler.SchemeName, _ => { });
                    services.Configure<AuthenticationOptions>(options =>
                        (options.DefaultAuthenticateScheme, options.DefaultChallengeScheme) = (UserHandler.SchemeName, UserHandler.SchemeName));
                    services.AddGenericPlatform(configuration);

                    services.AddScoped<PreferencesDbContext>(_ => new PreferencesTestDbContext(_database));
                    services.AddScoped<IExternalDomainStore, ExternalDomainStore>();
                    services.AddSingleton(Mock.Of<IClientSecretProtector>());
                    services.AddSingleton(Mock.Of<IOptionsMonitor<MailOptions>>(m => m.CurrentValue == new MailOptions()));
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }))
            .StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private Task<HttpResponseMessage> GetAsync(string? user)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "api/Admin/domains/external");
        if (user is not null) request.Headers.Add(UserHeader, user);
        return _client.SendAsync(request);
    }

    [Fact]
    public async Task AConfiguredAdministrator_ListsTheExternalDomains()
    {
        using var response = await GetAsync("Michael@exemple.be");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnotherUser_Gets403()
    {
        using var response = await GetAsync("anne@exemple.be");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnAnonymousCaller_Gets401()
    {
        using var response = await GetAsync(null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class OnlyController : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo) => typeInfo.AsType() == typeof(ExternalDomainsController);
    }

    private sealed class UserHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal const string SchemeName = "TestUser";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var user)) return Task.FromResult(AuthenticateResult.NoResult());

            var parts = user.ToString().Split('@');
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Upn, parts[0]), new Claim(ClaimTypes.Dns, parts[1])], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
