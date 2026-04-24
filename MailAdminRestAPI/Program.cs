using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using weesky.MailAdminRestAPI.Authentication.Extensions;
using weesky.MailAdminRestAPI.Authentication.Models;
using weesky.MailAdminRestAPI.Authentication.Services;
using weesky.MailAdminRestAPI.Data;
using weesky.MailAdminRestAPI.Models;
using weesky.MailAdminRestAPI.Repositories;
using weesky.MailAdminRestAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var connectionString = builder.Configuration.GetConnectionString("MailUserAccountsDatabase");

builder.Services.AddDbContext<ApplicationDbContext>(options => {
	options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
	.LogTo(Console.WriteLine, LogLevel.Warning);
});

builder.Services.AddOptions<TokenConstants>().Configure(options => builder.Configuration.GetSection("TokenConstants").Bind(options));
builder.Services.AddOptions<DovecotOptions>().Bind(builder.Configuration.GetSection("Dovecot"));
builder.Services.AddScoped<IUsersRepository, UsersRepository>();
builder.Services.AddScoped<IAliasesRepository, AliasesRepository>();
builder.Services.AddScoped<IUserAuthenticator, UserAuthenticator>();
builder.Services.AddScoped<ITokenManager, TokenManager>();
builder.Services.AddHttpClient<IDovecotQuotaClient, DovecotQuotaClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddJwtBearerAuthentication(true);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "PATCH", "DELETE")
            .WithHeaders("Authorization", "Content-Type")
            .AllowCredentials()
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});

builder.Services.AddProblemDetails();

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
        Title = "Milkyway",
        Description = "weesky.net mail administration API",
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

app.MapControllers();

app.Run();
