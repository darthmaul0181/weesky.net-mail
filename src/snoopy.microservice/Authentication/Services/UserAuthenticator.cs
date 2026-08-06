using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Authentication.Services;

public sealed class UserAuthenticator(
    IUsersRepository usersRepository,
    ITokenManager tokenManager,
    IWebmailUserStore webmailUsers,
    ILogger<UserAuthenticator> logger) : IUserAuthenticator
{
    public async Task<Result<AuthToken>> AuthenticateAsync(string email, string password, CancellationToken cancellationToken)
    {
        var check = await usersRepository.VerifyCredentialsAsync(email, password, cancellationToken);

        if (check.User is not { } user)
        {
            // The reason is logged, never answered. Every cause — no such mailbox, a deactivated
            // one, a wrong password — gets the same message, and the check above takes the same
            // time for all three, so neither the body nor the clock tells them apart.
            logger.LogInformation(
                "Audit: login email={Email} outcome=failure reason={Reason}", email, AuditReason(check.Result));
            return Result.Failure<AuthToken>("Authentication failed");
        }

        logger.LogInformation("Audit: login email={Email} outcome=success", email);
        // The caller's token, not None: this upsert precedes the only durable effect of a login —
        // the cookies — and the token cannot be built without the id and stamp it returns, so an
        // abandoned login has nothing left half-done and the next one writes the same row again.
        var account = await webmailUsers.RegisterLoginAsync(user.Email, cancellationToken);
        user.WebmailUid = account.Id;
        user.SecurityStamp = account.SecurityStamp;
        return Result.Success(tokenManager.Generate(user));
    }

    /// <summary>
    /// The token written to the audit log. Spelled out here rather than letting the enum's own
    /// name through: these lines get grepped, so the wording is an interface. <c>bad_password</c>
    /// is the one this log has always used, and it keeps it.
    /// </summary>
    internal static string AuditReason(CredentialResult result) => result switch
    {
        CredentialResult.UnknownAccount => "unknown_account",
        CredentialResult.Deactivated => "deactivated",
        CredentialResult.WrongPassword => "bad_password",
        _ => "unknown"
    };
}
