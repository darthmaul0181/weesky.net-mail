using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Providers.Weesky.Authorization;
using weesky.Snoopy.Providers.Weesky.Data;
using weesky.Snoopy.Providers.Weesky.HealthChecks;
using weesky.Snoopy.Providers.Weesky.Models;
using weesky.Snoopy.Providers.Weesky.Platform;
using weesky.Snoopy.Providers.Weesky.Repositories;
using weesky.Snoopy.Providers.Weesky.Services;

namespace weesky.Snoopy.Providers.Weesky;

/// <summary>
/// The weesky.net platform: a dovecot database of mailboxes, domains and aliases, a doveadm HTTP
/// API, and the admin flag that governs them. Everything this assembly holds hangs off the single
/// <c>Weesky</c> configuration section, so a deployment that does not run this platform configures
/// none of it — and, not calling <see cref="AddWeeskyPlatform"/>, loads none of it either.
///
/// The type also marks the assembly for <c>AddApplicationPart</c>: the controllers here only exist
/// as routes when the host puts this assembly in front of MVC.
/// </summary>
public static class WeeskyPlatform
{
    /// <summary>The configuration section every weesky setting lives under.</summary>
    public const string SectionName = "Weesky";

    /// <summary>The dovecot database's connection string, under <see cref="SectionName"/>.</summary>
    public const string ConnectionStringKey = $"{SectionName}:ConnectionStrings:MailUserAccountsDatabase";

    /// <summary>The doveadm HTTP API endpoint, under <see cref="SectionName"/>.</summary>
    public const string DovecotApiUrlKey = $"{SectionName}:Dovecot:ApiUrl";

    /// <summary>The doveadm shared key, under <see cref="SectionName"/>.</summary>
    public const string DovecotApiKeyKey = $"{SectionName}:Dovecot:ApiKey";

    public static IServiceCollection AddWeeskyPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);

        // Only the Weesky-scoped key is read: the root-level one this replaced is what a
        // pre-split deployment still carries, and honouring it would let a half-migrated
        // configuration start. The refusal names the key in full so the move is unambiguous.
        var accounts = new MySqlConnector.MySqlConnectionStringBuilder(
            section.GetSection("ConnectionStrings")["MailUserAccountsDatabase"] is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringKey}' is missing. The weesky platform " +
                    "administers its mailboxes from the dovecot database; refusing to start rather " +
                    "than answering every account, alias and admin request from nothing. A deployment " +
                    "without one belongs on \"Platform\": \"generic\"."))
        {
            ConvertZeroDateTime = true
        }.ToString();

        // Detected once at startup: ServerVersion.AutoDetect opens a connection, so it must not
        // run on every DbContext instantiation.
        var accountsVersion = ServerVersion.AutoDetect(accounts);

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseMySql(accounts, accountsVersion, mySql => mySql.EnableStringComparisonTranslations()));

        services.AddDovecotOptions(section);

        services.AddHttpClient<IDovecotQuotaClient, DovecotQuotaClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        services.AddHealthChecks().AddCheck<WeeskyDatabaseHealthCheck>("weesky-database");

        // Swagger reads the comment files off the running binary's directory, and this assembly
        // brings its own; without this the platform's endpoints document as bare signatures.
        var documentation = Path.Combine(AppContext.BaseDirectory, "snoopy.providers.weesky.xml");
        if (File.Exists(documentation))
            services.ConfigureSwaggerGen(swagger => swagger.IncludeXmlComments(documentation));

        return services.AddWeeskyPlatformServices();
    }

    /// <summary>
    /// Binds the doveadm settings and refuses a deployment that left either half out. Validated on
    /// start rather than on first use: without this, a service missing <c>Weesky__Dovecot__ApiKey</c>
    /// starts perfectly and the admin quota column is the first thing to say otherwise.
    /// </summary>
    internal static IServiceCollection AddDovecotOptions(this IServiceCollection services, IConfiguration weesky)
    {
        services.AddOptions<DovecotOptions>()
            .Bind(weesky.GetSection("Dovecot"))
            .Validate(
                options => Uri.TryCreate(options.ApiUrl, UriKind.Absolute, out _),
                $"'{DovecotApiUrlKey}' is missing or is not an absolute URL. The weesky platform reads " +
                "mailbox quota from the doveadm HTTP API; set it to the full endpoint, for instance " +
                "https://mail.example.net:8080/doveadm/v1.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiKey),
                $"'{DovecotApiKeyKey}' is missing. It is the doveadm_api_key the API authenticates with; " +
                "set Weesky__Dovecot__ApiKey in the service's EnvironmentFile.")
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Everything the platform registers except the dovecot <see cref="ApplicationDbContext"/>,
    /// which is the one piece that needs a reachable server to register. Split out so the wiring
    /// can be exercised whole against an in-memory context.
    /// </summary>
    internal static IServiceCollection AddWeeskyPlatformServices(this IServiceCollection services)
    {
        services.AddScoped<IUsersRepository, UsersRepository>();
        services.AddScoped<IAliasesRepository, AliasesRepository>();
        services.AddScoped<IAdminRepository, AdminRepository>();

        // The policy itself is declared by the core; this is the handler that can answer it, and
        // the reason a deployment on another platform can never satisfy it.
        services.AddScoped<IAuthorizationHandler, AdminRequirementHandler>();

        // Scoped, since the adapters ride on the repositories' own per-request DbContext.
        services.AddScoped<IAliasDirectory, WeeskyAliasDirectory>();
        services.AddScoped<IProfileReader, WeeskyProfileReader>();
        services.AddScoped<IAccountInfoProvider, WeeskyAccountInfoProvider>();

        return services;
    }
}
