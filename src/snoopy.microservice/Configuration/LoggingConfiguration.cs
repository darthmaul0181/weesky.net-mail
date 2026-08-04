using Serilog;
using Serilog.Events;
using Serilog.Filters;

namespace weesky.Snoopy.Microservice.Configuration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
internal static class LoggingConfiguration
{
    /// <summary>Serilog writes to this assembly's own request-logging category.</summary>
    private const string RequestLoggerSource = "Serilog.AspNetCore.RequestLoggingMiddleware";

    /// <summary>
    /// Two daily files at Information — HTTP requests apart from the rest, so neither buries the
    /// other. Two categories are turned down and must stay down: Microsoft.AspNetCore, and
    /// EF Core's Database.Command, which logs every executed statement at Information and buried
    /// the log under SQL. Warning still surfaces command failures, which EF logs at Error under
    /// that same category.
    /// </summary>
    public static IHostBuilder UseSnoopyLogging(this IHostBuilder host)
    {
        var logDirectory = OperatingSystem.IsWindows()
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : "/var/log/snoopy.microservice";
        Directory.CreateDirectory(logDirectory);

        return host.UseSerilog((ctx, cfg) =>
        {
            var logPrefix = ctx.HostingEnvironment.IsProduction()
                ? ""
                : $"{ctx.HostingEnvironment.EnvironmentName.ToLowerInvariant()}-";

            cfg
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Logger(l => l
                    .Filter.ByIncludingOnly(Matching.FromSource(RequestLoggerSource))
                    .WriteTo.File(
                        Path.Combine(logDirectory, $"log-{logPrefix}http-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 31,
                        shared: true))
                .WriteTo.Logger(l => l
                    .Filter.ByExcluding(Matching.FromSource(RequestLoggerSource))
                    .WriteTo.File(
                        Path.Combine(logDirectory, $"log-{logPrefix}.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 31,
                        shared: true));
        });
    }

    /// <summary>
    /// The health probe runs constantly; at Information it is the whole HTTP log.
    ///
    /// The caller's address is named in the line, which the default template does not carry: it is
    /// what a 401 or a 429 has to be read against — one address failing five logins is a user who
    /// forgot their password, five hundred addresses failing one each is not — and it is the value
    /// the login limiter partitions on, so this log is also how one sees whether
    /// <see cref="SecurityConfiguration.AddProxyForwardedHeaders"/> is doing its job. Set from the
    /// connection rather than from the header, so what appears here is the address the limiter used.
    /// </summary>
    public static IApplicationBuilder UseSnoopyRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "HTTP {RequestMethod} {RequestPath} from {ClientIp} responded {StatusCode} in {Elapsed:0.0000} ms";

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
                diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

            options.GetLevel = (ctx, _, _) =>
                ctx.Request.Path == "/health" ? LogEventLevel.Verbose : LogEventLevel.Information;
        });
}
