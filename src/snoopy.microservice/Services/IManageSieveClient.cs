using CSharpFunctionalExtensions;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Opens authenticated ManageSieve sessions on behalf of any mailbox user, using the
/// master credentials configured in <c>SieveOptions</c>.
/// </summary>
public interface IManageSieveClient
{
    /// <summary>
    /// Opens a TLS-protected ManageSieve session authenticated as <paramref name="targetUser"/>
    /// via SASL PLAIN master impersonation. The returned session must be disposed.
    /// </summary>
    /// <param name="targetUser">Full mailbox address (e.g. <c>alice@weesky.be</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<IManageSieveSession>> OpenSessionAsync(string targetUser, CancellationToken cancellationToken = default);
}
