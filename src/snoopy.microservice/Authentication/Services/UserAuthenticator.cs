using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Authentication.Services;

public sealed class UserAuthenticator(
    IImapConnectionFactory factory,
    IOptionsMonitor<MailOptions> mail,
    ITokenManager tokenManager,
    IWebmailUserStore webmailUsers,
    ILogger<UserAuthenticator> logger) : IUserAuthenticator
{
    public async Task<Result<AuthToken>> AuthenticateAsync(string email, string password, CancellationToken cancellationToken)
    {
        // Lower-cased before the IMAP LOGIN (Roundcube's login_lowercase model): a case-sensitive
        // IMAP server would otherwise refuse a mixed-case spelling, and the webmail store keys off
        // this same canonical spelling regardless.
        var canonicalEmail = IdentityResolver.Canonical(email);
        var connection = MailConnectionBuilder.Home(
            mail.CurrentValue, MailAccountConnection.Primary, canonicalEmail, new PasswordCredential(password));

        var opened = await factory.OpenAsync(connection, cancellationToken);
        if (opened.IsFailure)
        {
            // Every cause — wrong password, unknown mailbox, an unreachable server — answers the
            // same message; the detail goes to the log alone, and the fine-grained
            // anti-enumeration work lives at Dovecot, not here.
            logger.LogInformation("Audit: login email={Email} outcome=failure reason=imap_no", canonicalEmail);
            return Result.Failure<AuthToken>("Authentication failed");
        }
        await opened.Value.DisposeAsync();

        logger.LogInformation("Audit: login email={Email} outcome=success", canonicalEmail);
        // The caller's token, not None: this upsert precedes the only durable effect of a login —
        // the cookies — and the token cannot be built without the id and stamp it returns, so an
        // abandoned login has nothing left half-done and the next one writes the same row again.
        var account = await webmailUsers.RegisterLoginAsync(canonicalEmail, cancellationToken);
        var user = new User(canonicalEmail) { WebmailUid = account.Id, SecurityStamp = account.SecurityStamp };
        return Result.Success(tokenManager.Generate(user));
    }
}
