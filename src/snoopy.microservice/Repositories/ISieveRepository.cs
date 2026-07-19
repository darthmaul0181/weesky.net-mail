using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// High-level Sieve management API used by controllers. Each method opens its own
/// ManageSieve session on behalf of the supplied user.
/// </summary>
public interface ISieveRepository
{
    Task<Result<SieveRuleSet>> GetRuleSetAsync(User user, CancellationToken cancellationToken = default);

    Task<Result<string>> GetRawScriptAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compile the supplied rules with the chosen provider (defaults to the registry's
    /// default when <paramref name="providerId"/> is null) and write the result back to
    /// <paramref name="scriptName"/> (or the provider's default name when null), then
    /// activate that script.
    /// </summary>
    Task<Result> SaveRulesAsync(User user, IReadOnlyList<SieveRule> rules, string? providerId, string? scriptName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replace the user's script with raw Sieve text. Used by the advanced editor.
    /// </summary>
    Task<Result> SaveRawScriptAsync(User user, string content, string? scriptName, CancellationToken cancellationToken = default);

    /// <summary>Deactivate and delete the managed script. No-op if it does not exist.</summary>
    Task<Result> DeleteAllRulesAsync(User user, CancellationToken cancellationToken = default);
}
