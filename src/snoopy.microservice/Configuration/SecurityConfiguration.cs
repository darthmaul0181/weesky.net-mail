using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using weesky.Snoopy.Microservice.Authentication.Authorization;
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

        services.AddScoped<IAuthorizationHandler, AdminRequirementHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AdminRequirement.PolicyName, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new AdminRequirement()));
        });

        services.AddMemoryCache();
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
                .WithHeaders("Authorization", "Content-Type")
                .AllowCredentials()
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10)));
        });
    }

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
