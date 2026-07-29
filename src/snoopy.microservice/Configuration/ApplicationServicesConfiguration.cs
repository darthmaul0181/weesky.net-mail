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
        services.AddOptions<TokenConstants>().Bind(configuration.GetSection("TokenConstants"));
        services.AddOptions<DovecotOptions>().Bind(configuration.GetSection("Dovecot"));
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

        return services;
    }

    /// <summary>Everything that talks to the mail server: IMAP, SMTP, ManageSieve, doveadm.</summary>
    public static IServiceCollection AddMailServices(this IServiceCollection services)
    {
        services.AddSingleton<IImapConnectionFactory, ImapConnectionFactory>();
        services.AddSingleton<ISmtpConnectionFactory, SmtpConnectionFactory>();
        services.AddSingleton<IManageSieveClient, ManageSieveClient>();
        services.AddSingleton<IMailHtmlSanitizer, MailHtmlSanitizer>();
        services.AddSingleton<IOutgoingMailSanitizer, OutgoingMailSanitizer>();
        services.AddSingleton<IQuotePreparer, QuotePreparer>();

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

        services.AddHttpClient<IDovecotQuotaClient, DovecotQuotaClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });

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
        services.AddScoped<IUsersRepository, UsersRepository>();
        services.AddScoped<IAliasesRepository, AliasesRepository>();
        services.AddScoped<IAdminRepository, AdminRepository>();
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

        return services;
    }
}
