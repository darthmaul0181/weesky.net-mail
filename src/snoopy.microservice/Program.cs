using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Serilog;
using Serilog.Events;
using Serilog.Filters;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Authentication.Extensions;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.HealthChecks;
using weesky.Snoopy.Microservice.RuleProviders;
using weesky.Snoopy.Microservice.RuleProviders.Rainloop;
using weesky.Snoopy.Microservice.Services;

var builder = WebApplication.CreateBuilder(args);

var logDirectory = OperatingSystem.IsWindows()
    ? Path.Combine(AppContext.BaseDirectory, "logs")
    : "/var/log/snoopy.microservice";
Directory.CreateDirectory(logDirectory);

const string requestLoggerSource = "Serilog.AspNetCore.RequestLoggingMiddleware";

builder.Host.UseSerilog((ctx, cfg) =>
{
    var logPrefix = ctx.HostingEnvironment.IsProduction()
        ? ""
        : $"{ctx.HostingEnvironment.EnvironmentName.ToLowerInvariant()}-";
    cfg
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Logger(l => l
            .Filter.ByIncludingOnly(Matching.FromSource(requestLoggerSource))
            .WriteTo.File(
                Path.Combine(logDirectory, $"log-{logPrefix}http-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                shared: true))
        .WriteTo.Logger(l => l
            .Filter.ByExcluding(Matching.FromSource(requestLoggerSource))
            .WriteTo.File(
                Path.Combine(logDirectory, $"log-{logPrefix}.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                shared: true));
});

// Add services to the container.

var connectionString = new MySqlConnector.MySqlConnectionStringBuilder(
	builder.Configuration.GetConnectionString("MailUserAccountsDatabase")
		?? throw new InvalidOperationException("Connection string 'MailUserAccountsDatabase' is missing"))
{
	ConvertZeroDateTime = true
}.ToString();

// Detect the server version once at startup: ServerVersion.AutoDetect opens a database
// connection, so it must not run on every DbContext instantiation.
var serverVersion = ServerVersion.AutoDetect(connectionString);

builder.Services.AddDbContext<ApplicationDbContext>(options => {
	options.UseMySql(connectionString, serverVersion, mySqlOptions => mySqlOptions.EnableStringComparisonTranslations())
	.LogTo(Console.WriteLine, LogLevel.Warning);
});

builder.Services.AddOptions<TokenConstants>().Configure(options => builder.Configuration.GetSection("TokenConstants").Bind(options));
builder.Services.AddOptions<DovecotOptions>().Bind(builder.Configuration.GetSection("Dovecot"));
builder.Services.AddOptions<SieveOptions>().Bind(builder.Configuration.GetSection("Sieve"));
builder.Services.AddSingleton<IManageSieveClient, ManageSieveClient>();
builder.Services.AddSingleton<IRuleProvider, WeeskyRuleProvider>();
builder.Services.AddSingleton<IRuleProvider, RainloopRuleProvider>();
builder.Services.AddSingleton<IRuleProviderRegistry, RuleProviderRegistry>();
builder.Services.AddScoped<ISieveRepository, SieveRepository>();
builder.Services.AddScoped<IUsersRepository, UsersRepository>();
builder.Services.AddScoped<IAliasesRepository, AliasesRepository>();
builder.Services.AddScoped<IAdminRepository, AdminRepository>();
builder.Services.AddScoped<IUserAuthenticator, UserAuthenticator>();
builder.Services.AddScoped<ITokenManager, TokenManager>();
builder.Services.AddHttpClient<IDovecotQuotaClient, DovecotQuotaClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddJwtBearerAuthentication(true);

builder.Services.AddScoped<IAuthorizationHandler, AdminRequirementHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AdminRequirement.PolicyName, policy =>
        policy.RequireAuthenticatedUser().AddRequirements(new AdminRequirement()));
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "PATCH", "DELETE", "PUT")
            .WithHeaders("Authorization", "Content-Type")
            .AllowCredentials()
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});

builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

builder.Services.AddRateLimiter(options =>
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

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.IgnoreNullValues = true;
    o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddEndpointsApiExplorer();


builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "v1",
        Title = "weesky.mail.snoopy",
        Description = "weesky.net mail account management",
        Contact = new OpenApiContact
        {
            Name = "administrator",
            Email = "root@weesky.net"
        }
    });

    var filePath = Path.Combine(AppContext.BaseDirectory, "ApiDocumentation.xml");
    options.IncludeXmlComments(filePath);
});

var app = builder.Build();

app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (ctx, _, _) =>
        ctx.Request.Path == "/health" ? LogEventLevel.Verbose : LogEventLevel.Information;
});

app.UseExceptionHandler();

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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class Program { }
