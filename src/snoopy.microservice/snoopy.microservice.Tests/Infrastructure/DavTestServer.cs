using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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

    /// <param name="email">the authenticated user's address</param>
    /// <param name="userId">the authenticated user's GUID, minted when absent</param>
    /// <param name="overrides">applied last, so a test may replace a registration — a counting or
    /// throwing repository being the use case</param>
    /// <param name="keepTransactionsFatal">leaves the InMemory refusal of BeginTransaction in
    /// place, so that a test can witness a caller opening a snapshot at all</param>
    internal static async Task<DavTestServer> StartAsync(
        string email = "someone@weesky.be", Guid? userId = null,
        Action<IServiceCollection>? overrides = null, bool keepTransactionsFatal = false)
    {
        var user = new DavTestUser(email, userId ?? Guid.NewGuid());
        var databaseName = Guid.NewGuid().ToString("N");

        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    ConfigureServices(services, user, databaseName, keepTransactionsFatal);
                    overrides?.Invoke(services);
                })
                .Configure(app =>
                {
                    app.Use(EnforceRequestSizeLimits);
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }))
            .StartAsync();

        return new DavTestServer(host, host.GetTestClient(), databaseName, user);
    }

    /// <summary>This instance's private InMemory database, for a test building a context of its
    /// own — a saboteur one, typically, which <see cref="CreateContext"/> cannot be.</summary>
    internal string DatabaseName => databaseName;

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

    /// <summary>
    /// The same request with no credentials at all: the test handler stands down, so the named
    /// policy runs against an anonymous caller exactly as it does over the wire.
    /// </summary>
    internal Task<DavTestResponse> SendUnauthenticated(string method, string path, string? body = null) =>
        SendAsync(method, path, body, headers: new Dictionary<string, string>
        {
            [TestCardDavAuthenticationHandler.NoCredentialsHeader] = "1",
        });

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    private static void ConfigureServices(IServiceCollection services, DavTestUser user,
        string databaseName, bool keepTransactionsFatal)
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

            manager.FeatureProviders.Add(new SelectedControllerFeatureProvider(
                typeof(CardDavController), typeof(WellKnownController)));
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
        services.AddScoped<PreferencesDbContext>(
            _ => new PreferencesTestDbContext(databaseName, keepTransactionsFatal));
        services.AddScoped<IDavContactReader, DavContactReader>();
        services.AddScoped<IContactSyncStore, ContactSyncStore>();
        services.AddScoped<ContactStore>();
        services.AddScoped<IDavContactWriter, DavContactWriter>();
    }

    /// <summary>
    /// TestServer implements no <see cref="IHttpMaxRequestBodySizeFeature"/>, so a
    /// <c>[RequestSizeLimit]</c> would silently not apply and every 413 assertion here would be
    /// vacuous. This reproduces Kestrel's enforcement: the attribute's filter sets the feature's
    /// limit, a read past it throws the 413 <see cref="BadHttpRequestException"/> Kestrel throws,
    /// and the middleware turns it into the status, exactly as Kestrel would.
    /// </summary>
    private static async Task EnforceRequestSizeLimits(HttpContext context, Func<Task> next)
    {
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(new BodySizeLimitFeature(context));
        try
        {
            await next();
        }
        catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = ex.StatusCode;
        }
    }

    /// <summary>Admits the named controllers only: the default provider would publish every
    /// controller of the assembly, whose dependencies this host deliberately does not resolve.
    /// </summary>
    private sealed class SelectedControllerFeatureProvider(params Type[] controllers)
        : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo) =>
            controllers.Contains(typeInfo.AsType());
    }

    private sealed class BodySizeLimitFeature(HttpContext context) : IHttpMaxRequestBodySizeFeature
    {
        private long? maxRequestBodySize;

        public bool IsReadOnly => false;

        public long? MaxRequestBodySize
        {
            get => maxRequestBodySize;
            set
            {
                maxRequestBodySize = value;
                if (value is { } limit) context.Request.Body = new LimitedBody(context.Request.Body, limit);
            }
        }
    }

    private sealed class LimitedBody(Stream inner, long limit) : Stream
    {
        private long read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Counted(await inner.ReadAsync(buffer, cancellationToken));

        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            Counted(inner.Read(buffer, offset, count));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        private int Counted(int count)
        {
            read += count;
            return read > limit
                ? throw new BadHttpRequestException(
                    "Request body too large.", StatusCodes.Status413PayloadTooLarge)
                : count;
        }
    }
}
