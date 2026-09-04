using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Runs on every mail request, so it spends nothing it does not need: the primary account never
/// touches the database, and the KEK is only derived when a v1 cookie carries none — in which
/// case the cookie is re-issued as v2 so the derivation happens once, not once per request.
/// </summary>
internal sealed class AccountConnectionResolver(
    IMailCredentialStore credentials,
    IConnectedAccountStore accounts,
    IExternalDomainStore domains,
    IWebmailUserStore users,
    IOAuthTokenService oauth,
    IOptionsMonitor<MailOptions> options,
    IOptions<TokenConstants> tokenConstants,
    RequestIdentity identity,
    ILogger<AccountConnectionResolver> logger) : IAccountConnectionResolver
{
    public async Task<Result<MailAccountConnection>> ResolveAsync(
        User user, HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        identity.Set(user.WebmailUid);

        var retrieved = credentials.Retrieve(request);
        if (retrieved.IsFailure) return Result.Failure<MailAccountConnection>(retrieved.Error);
        var payload = retrieved.Value;

        var accountId = IAccountConnectionResolver.AccountIdFrom(request);
        if (accountId is null)
            return HomeConnection(MailAccountConnection.Primary, user.Email, new PasswordCredential(payload.Password));

        // An id that parses into nothing of the user's is simply not found — a foreign account
        // resolves to null by store scoping, indistinguishable from an unknown one by design.
        if (!Guid.TryParse(accountId, out var id))
            return Result.Failure<MailAccountConnection>(ConnectedAccountErrors.AccountNotFound);

        var row = await accounts.FindAsync(user.WebmailUid, id, cancellationToken);
        if (row is null)
            return Result.Failure<MailAccountConnection>(ConnectedAccountErrors.AccountNotFound);

        var kek = payload.Kek ?? await UpgradeCookieAsync(user, request, payload, cancellationToken);

        var context = ConnectedAccountCipher.Context(row);
        var secret = ConnectedAccountCipher.Decrypt(kek, row.Cipher, context, out var bound);
        if (secret.IsFailure) return Result.Failure<MailAccountConnection>(secret.Error);

        if (!bound) await BindCipherAsync(row, kek, secret.Value, context, cancellationToken);

        if (row.DomainId is null)
            return HomeConnection(row.Id.ToString(), row.Email, new PasswordCredential(secret.Value));

        return await ExternalConnection(row, secret.Value, kek, cancellationToken);
    }

    /// <summary>
    /// Rewrites a pre-binding cipher bound to its row, on the first request that opens it — which
    /// is what migrates the existing rows without asking anybody for a provider password again.
    ///
    /// Best effort on purpose: this sits on a read path, and the mailbox must open whether or not
    /// the rewrite lands. A failure costs one more unbound read; the next request tries again.
    /// </summary>
    private async Task BindCipherAsync(
        Data.Preferences.ConnectedAccount row, byte[] kek, string secret, byte[] context,
        CancellationToken cancellationToken)
    {
        try
        {
            await accounts.UpdateCipherAsync(
                row, ConnectedAccountCipher.Encrypt(kek, secret, context), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Could not bind the cipher of connected account {AccountId} to its row", row.Id);
        }
    }

    /// <summary>The primary and the local shared mailboxes: endpoints from appsettings.</summary>
    private MailAccountConnection HomeConnection(string accountId, string username, MailCredential credential) =>
        MailConnectionBuilder.Home(options.CurrentValue, accountId, username, credential);

    private async Task<Result<MailAccountConnection>> ExternalConnection(
        Data.Preferences.ConnectedAccount row, string secret, byte[] kek,
        CancellationToken cancellationToken)
    {
        var domain = await domains.FindAsync(row.DomainId!.Value, cancellationToken);
        if (domain is null)
            return Result.Failure<MailAccountConnection>(ConnectedAccountErrors.AccountNotFound);

        var credential = await CredentialFor(row, domain, secret, kek, cancellationToken);
        if (credential.IsFailure) return Result.Failure<MailAccountConnection>(credential.Error);

        if (!MailConnectionBuilder.TryExternal(
                domain, row.Id.ToString(), row.Email, credential.Value, out var connection,
                options.CurrentValue.AllowCleartext))
        {
            logger.LogError(
                "External domain {DomainName} ({DomainId}) holds an unusable security or OAuth value",
                domain.Name, domain.Id);
            return Result.Failure<MailAccountConnection>(ConnectedAccountErrors.AccountNotFound);
        }

        return connection;
    }

    /// <summary>
    /// The stored secret is a password on a password row and a refresh token on an OAuth one, so
    /// the row's own mode decides — never the domain's, which an admin may have flipped since.
    /// </summary>
    private async Task<Result<MailCredential>> CredentialFor(
        Data.Preferences.ConnectedAccount row, Data.Preferences.ExternalDomain domain, string secret,
        byte[] kek, CancellationToken cancellationToken)
    {
        if (row.AuthMode is not MailAuthMode.OAuth2)
            return Result.Success<MailCredential>(new PasswordCredential(secret));

        if (!OAuthProviderConfig.TryFrom(domain, out var provider))
        {
            logger.LogError(
                "External domain {DomainName} ({DomainId}) is OAuth but incompletely configured",
                domain.Name, domain.Id);
            return Result.Failure<MailCredential>(ConnectedAccountErrors.AccountNotFound);
        }

        var token = await oauth.GetAccessTokenAsync(row, provider, kek, cancellationToken);
        return token.IsSuccess
            ? Result.Success<MailCredential>(new OAuthCredential(token.Value))
            : Result.Failure<MailCredential>(token.Error);
    }

    /// <summary>
    /// A v1 cookie carries no KEK: derive it from the persisted salt and re-issue the cookie as
    /// v2, so the PBKDF2 cost is paid once instead of on every connected-account request.
    /// </summary>
    private async Task<byte[]> UpgradeCookieAsync(
        User user, HttpRequest request, MailCredentialPayload payload, CancellationToken cancellationToken)
    {
        var salt = await users.GetOrCreateKdfSaltAsync(user.Email, cancellationToken);
        var kek = ConnectedAccountCipher.DeriveKek(payload.Password, salt);

        credentials.Store(
            request.HttpContext.Response,
            new MailCredentialPayload(payload.Password, kek),
            TimeSpan.FromMinutes(tokenConstants.Value.ExpiryInMinutes));

        return kek;
    }
}
