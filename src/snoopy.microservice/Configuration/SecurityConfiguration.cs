using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Authentication.Extensions;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Configuration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
internal static class SecurityConfiguration
{
    public const string CorsPolicy = "Frontend";

    /// <summary>JWT bearer (cookie-backed), the admin policy, and the per-request session pieces.</summary>
    public static IServiceCollection AddSnoopyAuthentication(this IServiceCollection services)
    {
        services.AddJwtBearerAuthentication(cookiesSupport: true);

        // Basic over TLS, carrying the synchronisation secret. Registered as a scheme of its own
        // so it is never the default: a secret opens /dav and nothing else.
        services.AddAuthentication()
            .AddScheme<CardDavAuthenticationOptions, CardDavAuthenticationHandler>(
                CardDavAuthenticationDefaults.AuthenticationScheme, _ => { });

        // No handler is registered here: the platform brings one, and a deployment whose platform
        // has no admin directory leaves the policy unsatisfiable — the right answer for the two
        // core routes carrying it: PUT /api/AppSettings and POST /api/Contacts/Backfill (4a).
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AdminRequirement.PolicyName, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new AdminRequirement()));

            // Names this scheme alone, so the challenge is Basic and only Basic: a policy naming
            // both would emit WWW-Authenticate: Bearer first, and the handler already delegates to
            // the JWT when no Basic header is present. Declared here rather than in slice 4c-ii so
            // the challenge shape is settled once, in the tranche that owns it.
            options.AddPolicy(CardDavAuthenticationDefaults.PolicyName, policy => policy
                .AddAuthenticationSchemes(CardDavAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser());
        });

        services.AddMemoryCache();

        // Singleton is load-bearing: both memories live in this instance's dictionaries, and a
        // shorter lifetime would forget every burst at the end of the request that started it.
        services.AddSingleton<IDavAuthenticationCache, DavAuthenticationCache>();
        services.AddSingleton<AuthAttemptThrottle>();

        services.AddScoped<IMailCredentialStore, MailCredentialStore>();
        services.AddScoped<IUserAuthenticator, UserAuthenticator>();
        services.AddScoped<ITokenManager, TokenManager>();
        services.AddScoped<ISessionGuard, SessionGuard>();

        return services;
    }

    /// <summary>
    /// The frontend is the only origin allowed, and it must send cookies — both the JWT and the
    /// credentials one — so credentials are on and the origin list can never be a wildcard.
    ///
    /// The origins live in the environment, not in appsettings: the same build serves prod and
    /// dev, and each gets its own list from the systemd unit's EnvironmentFile
    /// (<c>Cors__AllowedOrigins__0=…</c>). Locally they come from launchSettings.json.
    /// </summary>
    public static IServiceCollection AddFrontendCors(this IServiceCollection services, IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        // There is no valid configuration of this API with no origin: it exists to serve a browser
        // frontend. Left empty, WithOrigins() refuses every cross-origin request and the webmail
        // dies with a CORS error in the console that reads like a network fault rather than a
        // missing variable. Refusing to start names the cause instead — same reason
        // AddCredentialKeyRing refuses without STATE_DIRECTORY.
        if (allowedOrigins.Length == 0)
        {
            throw new InvalidOperationException(
                "No CORS origin is configured. Set Cors__AllowedOrigins__0 in the service's " +
                "EnvironmentFile — for example Cors__AllowedOrigins__0=https://account.mail.weesky.net. " +
                "Additional origins are Cors__AllowedOrigins__1, __2, and so on.");
        }

        return services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicy, policy => policy
                .WithOrigins(allowedOrigins)
                .WithMethods("GET", "POST", "PATCH", "DELETE", "PUT")
                // The account header must pass preflight, or every mail request carrying it
                // dies on a CORS error before the server ever sees the account id.
                .WithHeaders("Authorization", "Content-Type", IAccountConnectionResolver.HeaderName)
                // Content-Disposition is not CORS-safelisted, so a browser hides it from
                // JavaScript unless the server exposes it explicitly — without this, the
                // client's filename extraction silently falls back and every download is named "attachment".
                .WithExposedHeaders("Content-Disposition")
                .AllowCredentials()
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10)));
        });
    }

    /// <summary>
    /// Restores the caller's own address on <c>RemoteIpAddress</c>, which is what
    /// <see cref="AddLoginRateLimiter"/> partitions on. Behind the reverse proxy every request
    /// arrives from the proxy, so without this the login limiter holds one bucket for the whole
    /// world: five attempts answer 429 to every user of the service, and no partition ever
    /// discriminates the source of a password-guessing run.
    ///
    /// The proxies are named in the environment rather than trusted by default, because the
    /// header is caller-supplied: a middleware that honours it from any peer hands anybody the
    /// ability to write their own partition key. The default known-proxy entries are cleared for
    /// the same reason — trust here is only ever what the deployment spelled out.
    /// </summary>
    public static IServiceCollection AddProxyForwardedHeaders(
        this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var knownProxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];

        // A proxy the configuration does not name makes the middleware drop the header silently:
        // the address stays the proxy's, the limiter goes back to one global bucket, and nothing
        // says so. Refusing to start names the cause instead — as AddFrontendCors and
        // AddCredentialKeyRing already do for their own silent failures.
        if (knownProxies.Length == 0 && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "No reverse proxy is configured. Set ForwardedHeaders__KnownProxies__0 in the service's " +
                "EnvironmentFile to the address the proxy connects from — 127.0.0.1 when it runs on this " +
                "host, plus ::1 if Kestrel listens on the IPv6 loopback. See " +
                "docs/superpowers/reverse-proxy-prerequisite.md. Refusing to start rather than " +
                "rate-limiting every account against one shared bucket.");
        }

        return services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // One hop: the proxy's own entry. Accepting more would let its client prepend an
            // address of its choosing and pick which partition it lands in.
            options.ForwardLimit = 1;

            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in knownProxies)
            {
                if (IPAddress.TryParse(proxy, out var address)) options.KnownProxies.Add(address);
                else throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownProxies holds '{proxy}', which is not an IP address.");
            }
        });
    }

    /// <summary>
    /// Bounds password guessing on the three endpoints that verify one: the login itself, attaching
    /// a mailbox, and re-entering an attached mailbox's password.
    ///
    /// The partition is the caller's address, which only means anything once
    /// <see cref="AddProxyForwardedHeaders"/> has put the real one there.
    /// </summary>
    public static IServiceCollection AddLoginRateLimiter(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("login", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromMinutes(1),
                        PermitLimit = 5,
                        QueueLimit = 0
                    }));
        });

    /// <summary>
    /// The Data Protection key ring encrypts the IMAP credentials cookie, so it must survive
    /// restarts: losing it makes every live credentials cookie undecryptable and signs every user
    /// out. systemd's StateDirectory= provides a directory outside the deployment path — which the
    /// release chmod/chown walk recursively — and owned by the service user.
    /// </summary>
    /// <returns>The key ring path, for the startup log line.</returns>
    public static string AddCredentialKeyRing(this IServiceCollection services, IWebHostEnvironment environment)
    {
        var stateDirectory = Environment.GetEnvironmentVariable("STATE_DIRECTORY")?.Split(':')[0];

        if (string.IsNullOrEmpty(stateDirectory) && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "STATE_DIRECTORY is not set. Add 'StateDirectory=snoopy.microservice' to the systemd unit. " +
                "Refusing to start rather than falling back to a key ring under the deployment directory.");
        }

        var keyRingPath = string.IsNullOrEmpty(stateDirectory)
            ? Path.Combine(environment.ContentRootPath, "keys")   // development only
            : Path.Combine(stateDirectory, "keys");

        Directory.CreateDirectory(keyRingPath);

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
            .SetApplicationName($"snoopy.microservice.{environment.EnvironmentName}");

        return keyRingPath;
    }

    /// <summary>
    /// This is an API: nothing it returns is meant to be rendered, framed or referred from. The
    /// Swagger UI is the one page that is, so it gets the only policy that allows its own assets.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Content-Security-Policy"] = context.Request.Path.StartsWithSegments("/swagger")
                ? "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'"
                : "default-src 'none'; frame-ancestors 'none'";
            await next();
        });
}
