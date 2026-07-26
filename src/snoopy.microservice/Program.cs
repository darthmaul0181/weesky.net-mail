using weesky.Snoopy.Microservice.Authentication.Middleware;
using weesky.Snoopy.Microservice.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSnoopyLogging();

builder.Services
    .AddSnoopyOptions(builder.Configuration)
    .AddSnoopyDatabases(builder.Configuration)
    .AddMailServices()
    .AddRuleProviders()
    .AddRepositories()
    .AddSnoopyAuthentication()
    .AddFrontendCors(builder.Configuration)
    .AddLoginRateLimiter()
    .AddApiDocumentation()
    .AddProblemDetails();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AttachmentSizeLimitFilter>();

var keyRingPath = builder.Services.AddCredentialKeyRing(builder.Environment);

builder.Services.AddControllers(MvcFormatterConfiguration.ConfigureFormatters).AddJsonOptions(o =>
{
    o.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var app = builder.Build();

app.Logger.LogInformation("Data Protection key ring: {KeyRingPath}", keyRingPath);

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
