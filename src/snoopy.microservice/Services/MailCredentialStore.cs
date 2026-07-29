using System.Security.Cryptography;
using System.Text;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.DataProtection;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Services;

internal sealed class MailCredentialStore : IMailCredentialStore
{
    /// <summary>Cookie name. Distinct from the JWT cookie so both can be cleared independently.</summary>
    public const string CookieName = "MailCredentials";

    private const string Purpose = "weesky.imap.credentials";

    /// <summary>Opens a payload carrying the KEK. Not a reserved prefix: a password spelling it
    /// without two base64 parts behind is still read back whole, as a v1 value.</summary>
    private const string V2Marker = "wm2|";

    private readonly IDataProtector _protector;

    public MailCredentialStore(IDataProtectionProvider provider)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));

        _protector = provider.CreateProtector(Purpose);
    }

    public void Store(HttpResponse response, MailCredentialPayload payload, TimeSpan lifetime)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));
        if (payload == null) throw new ArgumentNullException(nameof(payload));

        response.Cookies.Append(
            CookieName,
            _protector.Protect(Serialize(payload)),
            BuildOptions(DateTimeOffset.UtcNow.Add(lifetime)));
    }

    public Result<MailCredentialPayload> Retrieve(HttpRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        if (!request.Cookies.TryGetValue(CookieName, out var protectedValue) || string.IsNullOrEmpty(protectedValue))
        {
            return Result.Failure<MailCredentialPayload>("credentials_unavailable");
        }

        try
        {
            return Result.Success(Parse(_protector.Unprotect(protectedValue)));
        }
        catch (CryptographicException)
        {
            // Key ring lost or rotated away. Never log the payload.
            return Result.Failure<MailCredentialPayload>("credentials_unavailable");
        }
    }

    /// <summary>A payload without a KEK keeps the bare v1 shape, so a downgrade stays representable.</summary>
    private static string Serialize(MailCredentialPayload payload) =>
        payload.Kek is null
            ? payload.Password ?? string.Empty
            : V2Marker + Convert.ToBase64String(Encoding.UTF8.GetBytes(payload.Password ?? string.Empty))
              + "|" + Convert.ToBase64String(payload.Kek);

    private static MailCredentialPayload Parse(string raw)
    {
        if (!raw.StartsWith(V2Marker, StringComparison.Ordinal)) return new MailCredentialPayload(raw, null);

        // Base64 never contains '|', so the split cannot cut a v2 part in two.
        var parts = raw.Split('|');
        if (parts.Length != 3) return new MailCredentialPayload(raw, null);

        try
        {
            var kek = Convert.FromBase64String(parts[2]);
            // Empty base64 parses rather than throws, so the length is the real gate: accepting a
            // wrong-length key here would turn a password like "wm2||" into an empty one.
            if (kek.Length != ConnectedAccountCipher.KekLength) return new MailCredentialPayload(raw, null);

            return new MailCredentialPayload(Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])), kek);
        }
        catch (FormatException)
        {
            return new MailCredentialPayload(raw, null);
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
