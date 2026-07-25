using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Authentication.Services;

public sealed class UserAuthenticator : IUserAuthenticator
{
    private readonly IUsersRepository _usersRepository;
    private readonly ITokenManager _tokenManager;
    private readonly IWebmailUserStore _webmailUsers;
    private readonly ILogger<UserAuthenticator> _logger;

    public UserAuthenticator(IUsersRepository usersRepository, ITokenManager tokenManager, IWebmailUserStore webmailUsers, ILogger<UserAuthenticator> logger)
    {
        _usersRepository = usersRepository;
        _tokenManager = tokenManager;
        _webmailUsers = webmailUsers;
        _logger = logger;
    }

    public async Task<Result<AuthToken>> AuthenticateAsync(string email, string password)
    {
        var check = await _usersRepository.VerifyCredentialsAsync(email, password);

        if (check.User is not { } user)
        {
            // The reason is logged, never answered. Every cause — no such mailbox, a deactivated
            // one, a wrong password — gets the same message, and the check above takes the same
            // time for all three, so neither the body nor the clock tells them apart.
            _logger.LogInformation(
                "Audit: login email={Email} outcome=failure reason={Reason}", email, check.Result);
            return Result.Failure<AuthToken>("Authentication failed");
        }

        _logger.LogInformation("Audit: login email={Email} outcome=success", email);
        user.WebmailUid = await _webmailUsers.RegisterLoginAsync(user.Email, CancellationToken.None);
        return Result.Success(_tokenManager.Generate(user));
    }
}
