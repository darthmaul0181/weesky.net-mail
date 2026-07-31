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
    IOptionsMonitor<MailOptions> options,
    IOptions<TokenConstants> tokenConstants,
    ILogger<AccountConnectionResolver> logger) : IAccountConnectionResolver
{
    public async Task<Result<MailAccountConnection>> ResolveAsync(
        User user, HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);

        var retrieved = credentials.Retrieve(request);
        if (retrieved.IsFailure) return Result.Failure<MailAccountConnection>(retrieved.Error);
        var payload = retrieved.Value;

        var accountId = IAccountConnectionResolver.AccountIdFrom(request);
        if (accountId is null)
            return HomeConnection(MailAccountConnection.Primary, user.Email, payload.Password);

        // An id that parses into nothing of the user's is simply not found — a foreign account
        // resolves to null by store scoping, indistinguishable from an unknown one by design.
        if (!Guid.TryParse(accountId, out var id))
            return Result.Failure<MailAccountConnection>(ConnectedAccountErrors.AccountNotFound);

        var row = await accounts.FindAsync(user.WebmailUid, id, cancellationToken);
        if (row is null)
            return Result.Failure<MailAccountConnection>(ConnectedAccountErrors.AccountNotFound);

        var kek = payload.Kek ?? await UpgradeCookieAsync(user, request, payload, cancellationToken);

        var secret = ConnectedAccountCipher.Decrypt(kek, row.Cipher);
        if (secret.IsFailure) return Result.Failure<MailAccountConnection>(secret.Error);

        if (row.DomainId is null)
            return HomeConnection(row.Id.ToString(), row.Email, secret.Value);

        return await ExternalConnection(row, secret.Value, cancellationToken);
    }

    /// <summary>The primary and the local shared mailboxes: endpoints from appsettings.</summary>
    private MailAccountConnection HomeConnection(string accountId, string username, string password) =>
        MailConnectionBuilder.Home(options.CurrentValue, accountId, username, password);

    private async Task<Result<MailAccountConnection>> ExternalConnection(
        Data.Preferences.ConnectedAccount row, string secret, CancellationToken cancellationToken)
    {
        var domain = await domains.FindAsync(row.DomainId!.Value, cancellationToken);
        if (domain is null)
            return Result.Failure<MailAccountConnection>(ConnectedAccountErrors.AccountNotFound);

        if (!MailConnectionBuilder.TryExternal(
                domain, row.Id.ToString(), row.Email, secret, out var connection,
                options.CurrentValue.AllowCleartext))
        {
            logger.LogError(
                "External domain {DomainName} ({DomainId}) holds an unusable security value — " +
                "unknown, or None while Mail:AllowCleartext is off",
                domain.Name, domain.Id);
            return Result.Failure<MailAccountConnection>(ConnectedAccountErrors.AccountNotFound);
        }

        return connection;
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
