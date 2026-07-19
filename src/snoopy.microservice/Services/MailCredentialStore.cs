using System.Security.Cryptography;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.DataProtection;

namespace weesky.Snoopy.Microservice.Services;

internal sealed class MailCredentialStore : IMailCredentialStore
{
    /// <summary>Cookie name. Distinct from the JWT cookie so both can be cleared independently.</summary>
    public const string CookieName = "MailCredentials";

    private const string Purpose = "weesky.imap.credentials";

    private readonly IDataProtector _protector;

    public MailCredentialStore(IDataProtectionProvider provider)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));

        _protector = provider.CreateProtector(Purpose);
    }

    public void Store(HttpResponse response, string password, TimeSpan lifetime)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));

        response.Cookies.Append(
            CookieName,
            _protector.Protect(password ?? string.Empty),
            BuildOptions(DateTimeOffset.UtcNow.Add(lifetime)));
    }

    public Result<string> Retrieve(HttpRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        if (!request.Cookies.TryGetValue(CookieName, out var protectedValue) || string.IsNullOrEmpty(protectedValue))
        {
            return Result.Failure<string>("credentials_unavailable");
        }

        try
        {
            return Result.Success(_protector.Unprotect(protectedValue));
        }
        catch (CryptographicException)
        {
            // Key ring lost or rotated away. Never log the payload.
            return Result.Failure<string>("credentials_unavailable");
        }
    }

    public void Clear(HttpResponse response)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));

        response.Cookies.Append(CookieName, string.Empty, BuildOptions(DateTimeOffset.UnixEpoch));
    }

    private static CookieOptions BuildOptions(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Expires = expires
    };
}
