using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Scheduling;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// Routed for real, so the admin policy is what answers — not an attribute read by reflection. The
/// policy comes from the production registration; only the scheme and the handler stand in for the
/// JWT and the platform's admin directory.
/// </summary>
public sealed class SchedulingAccountAuthorizationTests : IAsyncLifetime
{
    private const string RoleHeader = "X-Test-Role";
    private const string StoredPassword = "n0t-f0r-y0ur-eyes";

    private readonly string _database = Guid.NewGuid().ToString("N");
    private readonly ServiceAccountSecretProtector _protector =
        new(DataProtectionProvider.Create(nameof(SchedulingAccountAuthorizationTests)));
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
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
                            manager.ApplicationParts.Add(new AssemblyPart(typeof(SchedulingAccountController).Assembly));
                            manager.FeatureProviders.Clear();
                            manager.FeatureProviders.Add(new OnlyController());
                        });
                    services.AddSnoopyAuthentication()
                        .AddAuthentication()
                        .AddScheme<AuthenticationSchemeOptions, RoleHandler>(RoleHandler.SchemeName, _ => { });
                    services.Configure<AuthenticationOptions>(options =>
                        (options.DefaultAuthenticateScheme, options.DefaultChallengeScheme) = (RoleHandler.SchemeName, RoleHandler.SchemeName));
                    services.AddSingleton<IAuthorizationHandler, AdminRoleHandler>();

                    services.AddScoped<PreferencesDbContext>(_ => new PreferencesTestDbContext(_database));
                    services.AddScoped<ISchedulingAccountStore, SchedulingAccountStore>();
                    services.AddSingleton<IServiceAccountSecretProtector>(_protector);
                    services.AddSingleton(Mock.Of<IServiceAccountProvider>());
                    services.AddSingleton(Mock.Of<ISmtpConnectionFactory>());
                    services.AddSingleton(Mock.Of<IOptionsMonitor<MailOptions>>(m => m.CurrentValue == new MailOptions()));
                    services.AddSingleton(TimeProvider.System);
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

    public static TheoryData<string, string> Verbs => new()
    {
        { "GET", "api/SchedulingAccount" },
        { "PUT", "api/SchedulingAccount" },
        { "DELETE", "api/SchedulingAccount" },
        { "POST", "api/SchedulingAccount/Test" },
    };

    private Task<HttpResponseMessage> SendAsync(string method, string path, string? role)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "PUT" or "POST")
            request.Content = new StringContent(
                """{"host":"smtp.weesky.be","port":587,"security":"StartTls","login":"a@weesky.be","password":"x"}""",
                Encoding.UTF8, "application/json");
        if (role is not null) request.Headers.Add(RoleHeader, role);
        return _client.SendAsync(request);
    }

    [Theory]
    [MemberData(nameof(Verbs))]
    public async Task AnAnonymousCaller_Gets401(string method, string path)
    {
        using var response = await SendAsync(method, path, role: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Verbs))]
    public async Task ANonAdministrator_Gets403(string method, string path)
    {
        using var response = await SendAsync(method, path, role: "user");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(await new SchedulingAccountStore(new PreferencesTestDbContext(_database)).FindAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AnAdministrator_ReadsTheAccount_AndTheWireCarriesNoPassword()
    {
        await new SchedulingAccountStore(new PreferencesTestDbContext(_database)).SaveAsync(
            "smtp.weesky.be", 587, "StartTls", "agenda@weesky.net", _protector.Protect(StoredPassword), CancellationToken.None);

        using var response = await SendAsync("GET", "api/SchedulingAccount", role: "admin");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """{"configured":true,"host":"smtp.weesky.be","port":587,"security":"StartTls","login":"agenda@weesky.net","passwordStored":true,"passwordReadable":true,"allowCleartext":false}""",
            json);
        Assert.DoesNotContain(StoredPassword, json);
    }

    [Fact]
    public async Task AnAdministrator_WithNothingStored_ReadsOnlyTheFlags()
    {
        using var response = await SendAsync("GET", "api/SchedulingAccount", role: "admin");

        Assert.Equal("""{"configured":false,"passwordStored":false,"passwordReadable":false,"allowCleartext":false}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheTestRoute_AcceptsAnEmptyBody_AndAnswers404WhenNothingIsStored()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/SchedulingAccount/Test");
        request.Headers.Add(RoleHeader, "admin");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class OnlyController : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo) => typeInfo.AsType() == typeof(SchedulingAccountController);
    }

    private sealed class RoleHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal const string SchemeName = "TestRole";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var role)) return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, role.ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class AdminRoleHandler : AuthorizationHandler<AdminRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
        {
            if (context.User.IsInRole("admin")) context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}
