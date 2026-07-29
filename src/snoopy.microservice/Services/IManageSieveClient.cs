using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Opens authenticated ManageSieve sessions against the target a <see cref="SieveConnection"/>
/// describes — our own server through master impersonation, or another provider's server as the
/// mailbox itself.
/// </summary>
public interface IManageSieveClient
{
    /// <summary>
    /// Opens a TLS-protected ManageSieve session on <paramref name="connection"/>, authenticating
    /// with SASL PLAIN. The returned session must be disposed.
    /// </summary>
    /// <param name="connection">Host, port and the two SASL identities plus their password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<IManageSieveSession>> OpenSessionAsync(SieveConnection connection, CancellationToken cancellationToken = default);
}
