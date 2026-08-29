using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// A really-routed host for the /dav surface, which is what a direct action call cannot exercise:
/// a PROPFIND verb reaching an action at all, the bare root outside /dav, a foreign {userId}, and
/// a 404 or 405 pronounced by the router rather than by code. MVC is restricted to
/// <see cref="CardDavController"/> alone — the assembly's other controllers would drag in
/// dependencies this host does not carry — the real policy name binds the real scheme name, whose
/// handler is replaced by one authenticating every request as one fixed user, and the
/// repositories run over the InMemory provider on a database private to this instance.
/// </summary>
/// <remarks>
/// The seam for every /dav verb is <see cref="SendAsync"/>: PROPFIND, GET, HEAD, OPTIONS, REPORT
/// and PROPPATCH are all one method name, one path, one optional body and whatever headers the
/// task needs — a per-verb wrapper is two lines in the test class that owns it.
/// </remarks>
internal sealed class DavTestServer : IAsyncDisposable
{
    private readonly IHost host;
    private readonly string databaseName;

    private DavTestServer(IHost host, HttpClient client, string databaseName, DavTestUser user)
    {
        this.host = host;
        this.databaseName = databaseName;
        Client = client;
        UserId = user.Uid;
        Email = user.Email;
    }

    internal HttpClient Client { get; }

    internal Guid UserId { get; }

    internal string Email { get; }

    internal static async Task<DavTestServer> StartAsync(
        string email = "someone@weesky.be", Guid? userId = null)
    {
        var user = new DavTestUser(email, userId ?? Guid.NewGuid());
        var databaseName = Guid.NewGuid().ToString("N");

        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services => ConfigureServices(services, user, databaseName))
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }))
            .StartAsync();

        return new DavTestServer(host, host.GetTestClient(), databaseName, user);
    }

    /// <summary>A context on this server's own database, for seeding and asserting.</summary>
    internal PreferencesTestDbContext CreateContext() => new(databaseName);

    internal Task<DavTestResponse> PropfindAsync(string path, string? depth, string? body) =>
        SendAsync("PROPFIND", path, body, depth);

    internal async Task<DavTestResponse> SendAsync(string method, string path, string? body = null,
        string? depth = null, IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/xml");
        if (depth is not null) request.Headers.Add("Depth", depth);
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
            request.Headers.TryAddWithoutValidation(name, value);

        using var response = await Client.SendAsync(request);
        return await DavTestResponse.ReadAsync(response);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    private static void ConfigureServices(IServiceCollection services, DavTestUser user, string databaseName)
    {
        services.AddLogging();

        services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            manager.ApplicationParts.Clear();
            manager.ApplicationParts.Add(new AssemblyPart(typeof(CardDavController).Assembly));
            for (var i = manager.FeatureProviders.Count - 1; i >= 0; i--)
            {
                if (manager.FeatureProviders[i] is ControllerFeatureProvider)
                    manager.FeatureProviders.RemoveAt(i);
            }

            manager.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(CardDavController)));
        });

        services.AddAuthentication(CardDavAuthenticationDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, TestCardDavAuthenticationHandler>(
                CardDavAuthenticationDefaults.AuthenticationScheme, _ => { });

        // The same two-line policy SecurityConfiguration registers under this name: the CardDav
        // scheme alone, an authenticated user, nothing else.
        services.AddAuthorization(options => options.AddPolicy(
            CardDavAuthenticationDefaults.PolicyName, policy => policy
                .AddAuthenticationSchemes(CardDavAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()));

        services.AddSingleton(user);
        services.AddScoped<PreferencesDbContext>(_ => new PreferencesTestDbContext(databaseName));
        services.AddScoped<IDavContactReader, DavContactReader>();
        services.AddScoped<IContactSyncStore, ContactSyncStore>();
    }

    /// <summary>Admits exactly one controller: the default provider would publish every controller
    /// of the assembly, whose dependencies this host deliberately does not resolve.</summary>
    private sealed class SingleControllerFeatureProvider(Type controller) : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo) => typeInfo.AsType() == controller;
    }
}
