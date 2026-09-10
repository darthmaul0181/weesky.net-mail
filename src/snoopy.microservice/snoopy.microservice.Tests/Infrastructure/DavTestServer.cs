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
using CSharpFunctionalExtensions;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Dav;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Fixtures;

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

    private DavTestServer(IHost host, HttpClient client, string databaseName, DavTestUser user,
        MutableTimeProvider clock)
    {
        this.host = host;
        this.databaseName = databaseName;
        Client = client;
        UserId = user.Uid;
        Email = user.Email;
        Clock = clock;
    }

    internal HttpClient Client { get; }

    internal Guid UserId { get; }

    internal string Email { get; }

    /// <summary>The host's own clock, frozen on a known date: an assertion on calendar-timezone or
    /// on a DTSTAMP must not depend on the day the suite runs.</summary>
    internal MutableTimeProvider Clock { get; }

    /// <param name="email">the authenticated user's address</param>
    /// <param name="userId">the authenticated user's GUID, minted when absent</param>
    /// <param name="overrides">applied last, so a test may replace a registration — a counting or
    /// throwing repository being the use case</param>
    /// <param name="keepTransactionsFatal">leaves the InMemory refusal of BeginTransaction in
    /// place, so that a test can witness a caller opening a snapshot at all</param>
    /// <param name="cardDav">the CardDAV switch the authenticated principal carries</param>
    /// <param name="calDav">the CalDAV switch the authenticated principal carries</param>
    internal static async Task<DavTestServer> StartAsync(
        string email = "someone@weesky.be", Guid? userId = null,
        Action<IServiceCollection>? overrides = null, bool keepTransactionsFatal = false,
        bool cardDav = true, bool calDav = true)
    {
        var user = new DavTestUser(email, userId ?? Guid.NewGuid(), cardDav, calDav);
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new MutableTimeProvider();

        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    ConfigureServices(services, user, databaseName, keepTransactionsFatal, clock);
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

        return new DavTestServer(host, host.GetTestClient(), databaseName, user, clock);
    }

    /// <summary>This instance's private InMemory database, for a test building a context of its
    /// own — a saboteur one, typically, which <see cref="CreateContext"/> cannot be.</summary>
    internal string DatabaseName => databaseName;

    /// <summary>A context on this server's own database, for seeding and asserting.</summary>
    internal PreferencesTestDbContext CreateContext() => new(databaseName);

    internal Task<DavTestResponse> PropfindAsync(string path, string? depth, string? body) =>
        SendAsync("PROPFIND", path, body, depth);

    internal async Task<DavTestResponse> SendAsync(string method, string path, string? body = null,
        string? depth = null, IReadOnlyDictionary<string, string>? headers = null,
        string contentType = "application/xml")
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, contentType);
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
    internal Task<DavTestResponse> SendUnauthenticated(string method, string path, string? body = null,
        string? depth = null) =>
        SendAsync(method, path, body, depth, new Dictionary<string, string>
        {
            [TestDavAuthenticationHandler.NoCredentialsHeader] = "1",
        });

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    private static void ConfigureServices(IServiceCollection services, DavTestUser user,
        string databaseName, bool keepTransactionsFatal, MutableTimeProvider clock)
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
                typeof(DavPrincipalController), typeof(CardDavController), typeof(CalDavController),
                typeof(WellKnownController)));
        });

        services.AddAuthentication(DavAuthenticationDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, TestDavAuthenticationHandler>(
                DavAuthenticationDefaults.AuthenticationScheme, _ => { });

        // The same two-line policy SecurityConfiguration registers under this name: the CardDav
        // scheme alone, an authenticated user, nothing else.
        services.AddAuthorization(options => options.AddPolicy(
            DavAuthenticationDefaults.PolicyName, policy => policy
                .AddAuthenticationSchemes(DavAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()));

        services.AddSingleton(user);
        // The HostBuilder here is a bare one: nothing registers a clock, so the calendar tables
        // would have none to read the year of calendar-timezone from.
        services.AddSingleton<TimeProvider>(clock);
        services.AddScoped<PreferencesDbContext>(
            _ => new PreferencesTestDbContext(databaseName, keepTransactionsFatal));
        services.AddScoped<IDavContactReader, DavContactReader>();
        services.AddScoped<IContactSyncStore, ContactSyncStore>();
        services.AddScoped<ContactStore>();
        services.AddScoped<IDavContactWriter, DavContactWriter>();
        services.AddScoped<IDavCalendarReader, DavCalendarReader>();
        // The rank of CalendarSyncStore is an INSERT … ON DUPLICATE KEY UPDATE the InMemory
        // provider cannot execute; the fixture honours the same contract in EF.
        services.AddScoped<ICalendarSyncStore, TestCalendarSyncStore>();
        services.AddScoped<CalendarStore>();
        services.AddScoped<ICalendarStore>(provider => provider.GetRequiredService<CalendarStore>());
        services.AddScoped<CalendarEventStore>();
        services.AddScoped<IDavCalendarWriter, DavCalendarWriter>();
        // The principal reads its addresses through these two. Answered by default with the
        // account's own domain and no curated identity, so no CardDAV test changes shape; a test
        // that cares replaces them through `overrides`.
        services.AddSingleton(DefaultAccounts(user));
        services.AddSingleton(EmptyIdentities());
    }

    private static IAccountInfoProvider DefaultAccounts(DavTestUser user)
    {
        var domain = user.Email[(user.Email.LastIndexOf('@') + 1)..];
        var provider = new Mock<IAccountInfoProvider>();
        provider.Setup(p => p.GetAccountInfoAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AccountInfo
            {
                Mailbox = "wsk", Domains = [new Domain { Id = "wsk", Name = domain }],
            }));
        return provider.Object;
    }

    private static ISendingIdentityStore EmptyIdentities()
    {
        var store = new Mock<ISendingIdentityStore>();
        store.Setup(s => s.GetAllAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return store.Object;
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
