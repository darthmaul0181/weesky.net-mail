using Serilog;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Sinks.SystemConsole.Themes;

namespace weesky.Scotty.Microservice.Configuration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
internal static class LoggingConfiguration
{
    /// <summary>Serilog writes to this assembly's own request-logging category.</summary>
    private const string RequestLoggerSource = "Serilog.AspNetCore.RequestLoggingMiddleware";

    // The file sink's own default, kept for the console so a line reads the same wherever it lands.
    private const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    public static IHostBuilder UseScottyLogging(this IHostBuilder host, IConfiguration configuration)
    {
        var output = ParseOutput(configuration["Logs:Output"]);
        var logDirectory = OperatingSystem.IsWindows()
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : "/var/log/scotty.microservice";

        return host.UseSerilog((ctx, cfg) => Configure(cfg, output, logDirectory,
            ctx.HostingEnvironment.IsProduction() ? "" : $"{ctx.HostingEnvironment.EnvironmentName.ToLowerInvariant()}-"));
    }

    internal static LogOutput ParseOutput(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "file" => LogOutput.File,
        "console" => LogOutput.Console,
        _ => throw new InvalidOperationException($"Logs:Output is \"{value}\"; possible values: file, console."),
    };

    /// <summary>
    /// Information, with two categories turned down that must stay down: Microsoft.AspNetCore, and
    /// EF Core's Database.Command, which logs every statement at Information and buried the log under
    /// SQL (Warning still surfaces its failures). Files: two daily ones, HTTP requests apart from the
    /// rest, so neither buries the other. Console: one stream, for a container's log collector.
    /// </summary>
    internal static LoggerConfiguration Configure(
        LoggerConfiguration logger, LogOutput output, string logDirectory, string logPrefix)
    {
        logger
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .Enrich.FromLogContext();

        if (output == LogOutput.Console)
            return logger.WriteTo.Console(outputTemplate: OutputTemplate, theme: ConsoleTheme.None);

        Directory.CreateDirectory(logDirectory);
        return logger
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
    public static IApplicationBuilder UseScottyRequestLogging(this IApplicationBuilder app) =>
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
