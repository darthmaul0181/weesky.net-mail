using System.Text;
using weesky.Snoopy.Microservice.Authentication.Extensions;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.RuleProviders;
using weesky.Snoopy.Microservice.RuleProviders.Rainloop;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Configuration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
internal static class ApplicationServicesConfiguration
{
    public static IServiceCollection AddSnoopyOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // The signing key is the whole of this API's authentication, and a short one is not refused
        // by the handler — HMAC takes any length. Validated on start rather than on first use, so
        // a misconfigured deployment fails where an operator is watching instead of on a 500 the
        // first user meets. Same refusal AddFrontendCors and AddCredentialKeyRing make.
        services.AddOptions<PlatformOptions>().Bind(configuration);
        services.AddOptions<TokenConstants>()
            .Bind(configuration.GetSection("TokenConstants"))
            .Validate(
                constants => Encoding.UTF8.GetByteCount(constants.Key ?? string.Empty)
                             >= AuthorizationExtension.MinimumSigningKeyBytes,
                $"TokenConstants:Key must be at least {AuthorizationExtension.MinimumSigningKeyBytes} bytes " +
                "to sign with HMAC-SHA256. Set TokenConstants__Key in the service's EnvironmentFile.")
            .ValidateOnStart();
        services.AddOptions<SieveOptions>().Bind(configuration.GetSection("Sieve"));
        services.AddOptions<MailOptions>().Bind(configuration.GetSection("Mail"));
        services.AddOptions<TrustedSenderOptions>().Bind(configuration.GetSection("TrustedSenders"));

        // Kept in step with the per-request cap AttachmentSizeLimitFilter applies. Left at its
        // 128 MB default it becomes the real ceiling whenever MaxMessageSizeMb is raised past it,
        // and the error a caller gets then no longer matches the configured limit.
        services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
        {
            var maxMessageSizeMb = configuration.GetSection("Mail").Get<MailOptions>()?.MaxMessageSizeMb
                ?? new MailOptions().MaxMessageSizeMb;
            options.MultipartBodyLengthLimit = (long)maxMessageSizeMb * 1024 * 1024 + 1024 * 1024;
        });

        services.AddEnveloppeModelStateResponse();

        return services;
    }

    /// <summary>Everything that talks to the mail server: IMAP, SMTP, ManageSieve, doveadm.</summary>
    public static IServiceCollection AddMailServices(this IServiceCollection services)
    {
        services.AddSingleton<IImapConnectionFactory, ImapConnectionFactory>();
        services.AddSingleton<ISmtpConnectionFactory, SmtpConnectionFactory>();
        services.AddSingleton<IManageSieveClient, ManageSieveClient>();
        services.AddSingleton<ISieveAvailabilityProbe, SieveAvailabilityProbe>();
        services.AddSingleton<IMailHtmlSanitizer, MailHtmlSanitizer>();
        services.AddSingleton<IOutgoingMailSanitizer, OutgoingMailSanitizer>();
        services.AddSingleton<IQuotePreparer, QuotePreparer>();
        services.AddSingleton<IClientSecretProtector, ClientSecretProtector>();
        // Singleton: a scoped store would forget every handshake at the end of the request that
        // started it.
        services.AddSingleton<IOAuthHandshakeStore, OAuthHandshakeStore>();

        // Scoped, so the whole request shares one authenticated IMAP connection and the container
        // closes it when the request ends. See ScopedImapSessionProvider.
        services.AddScoped<IImapSessionProvider, ScopedImapSessionProvider>();
        services.AddScoped<IAccountConnectionResolver, AccountConnectionResolver>();
        services.AddScoped<IOutgoingMessageFactory, OutgoingMessageFactory>();
        services.AddScoped<IMailSender, MailSender>();
        services.AddScoped<IDraftSaver, DraftSaver>();

        // Singleton is load-bearing: staged metadata and per-account reserved bytes live in this
        // instance's in-memory dictionaries, so a shorter lifetime would forget uploads mid-compose.
        services.AddSingleton<IStagedAttachmentStore, StagedAttachmentStore>();
        services.AddHostedService<StagedAttachmentSweeper>();
        services.AddHostedService<TrustedSenderSweeper>();

        services.AddHttpClient<IOAuthTokenService, OAuthTokenService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            // A 307/308 from the token endpoint would re-POST the client secret and the refresh
            // token to wherever it points; a redirecting provider is a refusal, not a destination.
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        return services;
    }

    /// <summary>The Sieve rule formats this server can read and write.</summary>
    public static IServiceCollection AddRuleProviders(this IServiceCollection services)
    {
        services.AddSingleton<IRuleProvider, WeeskyRuleProvider>();
        services.AddSingleton<IRuleProvider, RainloopRuleProvider>();
        services.AddSingleton<IRuleProviderRegistry, RuleProviderRegistry>();

        return services;
    }

    /// <summary>Database-backed stores, one scope per request alongside their DbContext.</summary>
    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<ISieveRepository, SieveRepository>();
        services.AddScoped<IMailFolderRepository, MailFolderRepository>();
        services.AddScoped<IMailMessageRepository, MailMessageRepository>();
        services.AddScoped<IFolderRoleStore, FolderRoleStore>();
        services.AddScoped<IUserPreferenceStore, UserPreferenceStore>();
        services.AddScoped<IAppSettingStore, AppSettingStore>();
        services.AddScoped<ISendingIdentityStore, SendingIdentityStore>();
        services.AddScoped<IWebmailUserStore, WebmailUserStore>();
        services.AddScoped<ITrustedSenderStore, TrustedSenderStore>();
        services.AddScoped<IContactStore, ContactStore>();
        services.AddScoped<IExternalDomainStore, ExternalDomainStore>();
        services.AddScoped<IConnectedAccountStore, ConnectedAccountStore>();
        services.AddScoped<IDavCredentialStore, DavCredentialStore>();

        return services;
    }
}
