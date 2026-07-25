using CSharpFunctionalExtensions;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// An authenticated ManageSieve session (RFC 5804) for a single target user.
/// Disposing closes the underlying TCP connection.
/// </summary>
public interface IManageSieveSession : IAsyncDisposable
{
    Task<Result<IReadOnlyList<SieveScriptListEntry>>> ListScriptsAsync(CancellationToken cancellationToken = default);

    Task<Result<string>> GetScriptAsync(string name, CancellationToken cancellationToken = default);

    Task<Result> PutScriptAsync(string name, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activate a script by name. Pass an empty string to deactivate all scripts.
    /// </summary>
    Task<Result> SetActiveAsync(string name, CancellationToken cancellationToken = default);

    Task<Result> DeleteScriptAsync(string name, CancellationToken cancellationToken = default);
}
