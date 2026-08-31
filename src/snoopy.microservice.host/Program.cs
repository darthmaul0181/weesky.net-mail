using weesky.Snoopy.Microservice.Authentication.Middleware;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Providers.Weesky;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSnoopyLogging();

// Read before anything is registered: the platform decides which directory answers for accounts,
// aliases and admin rights, and a deployment that does not say refuses to start.
var isWeesky = builder.Configuration.UsesWeeskyPlatform();

builder.Services
    .AddSnoopyOptions(builder.Configuration)
    .AddSnoopyDatabases(builder.Configuration)
    .AddMailServices()
    .AddRuleProviders()
    .AddRepositories()
    .AddSnoopyAuthentication()
    .AddFrontendCors(builder.Configuration)
    .AddProxyForwardedHeaders(builder.Configuration, builder.Environment)
    .AddLoginRateLimiter()
    .AddApiDocumentation()
    .AddProblemDetails();

if (isWeesky) builder.Services.AddWeeskyPlatform(builder.Configuration);
else builder.Services.AddGenericPlatform();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AttachmentSizeLimitFilter>();

var keyRingPath = builder.Services.AddCredentialKeyRing(builder.Environment);

var mvc = builder.Services
    .AddControllers(MvcFormatterConfiguration.ConfigureFormatters)
    .AddJsonOptions(MvcFormatterConfiguration.ConfigureJson);

// The host assembly carries no ApplicationPart attribute (see the csproj), so these two calls are
// the whole of what MVC sees: the core always, the platform only where it is loaded. Without the
// second, api/Admin, api/aliases and the two api/Account writes answer 404 rather than 500 out of
// a dovecot database a generic deployment does not have.
mvc.AddApplicationPart(typeof(ApiBaseController).Assembly);
if (isWeesky) mvc.AddApplicationPart(typeof(WeeskyPlatform).Assembly);

var app = builder.Build();

app.Logger.LogInformation("Data Protection key ring: {KeyRingPath}", keyRingPath);

// First of all: everything downstream that reads the caller's address — the request log and the
// login rate limiter above all — must see the client's, not the reverse proxy's.
app.UseForwardedHeaders();

app.UseSnoopyRequestLogging();
app.UseExceptionHandler();
app.UseSecurityHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(SecurityConfiguration.CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();

// After authentication so the principal exists, before authorization so a renewal still
// happens on a request that authorization will go on to reject for other reasons.
app.UseMiddleware<SlidingSessionMiddleware>();

app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class Program { }
