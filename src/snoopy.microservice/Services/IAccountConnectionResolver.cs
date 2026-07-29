using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The single place an account id from the request turns into hosts and credentials. The account
/// id travels in the <see cref="HeaderName"/> header, or the <see cref="QueryName"/> query
/// parameter, or defaults to the primary; only appsettings and the admin-defined external
/// domains can produce endpoints — no request field ever reaches a host.
/// </summary>
public interface IAccountConnectionResolver
{
    public const string HeaderName = "X-Account-Id";

    public const string QueryName = "account";

    /// <summary>
    /// The one place the transport is decoded: header first, then the query parameter. Null means
    /// the primary mailbox, as does the literal <see cref="MailAccountConnection.Primary"/>.
    /// </summary>
    public static string? AccountIdFrom(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? value = request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value)) value = request.Query[QueryName].FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) || value == MailAccountConnection.Primary ? null : value;
    }

    /// <summary>Failure codes: "credentials_unavailable" (401), "account_not_found" (404),
    /// <see cref="ConnectedAccountErrors.CredentialsInvalid"/> (409).</summary>
    Task<Result<MailAccountConnection>> ResolveAsync(
        User user, HttpRequest request, CancellationToken cancellationToken);
}
