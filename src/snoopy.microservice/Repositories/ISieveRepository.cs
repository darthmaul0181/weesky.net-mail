using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// High-level Sieve management API used by controllers. Each method opens its own
/// ManageSieve session on the supplied target — built by the controller, never derived here.
/// </summary>
public interface ISieveRepository
{
    Task<Result<SieveRuleSet>> GetRuleSetAsync(SieveConnection connection, CancellationToken cancellationToken = default);

    Task<Result<string>> GetRawScriptAsync(SieveConnection connection, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compile the supplied rules with the chosen provider (defaults to the registry's
    /// default when <paramref name="providerId"/> is null) and write the result back to
    /// <paramref name="scriptName"/> (or the provider's default name when null), then
    /// activate that script.
    /// </summary>
    Task<Result> SaveRulesAsync(SieveConnection connection, IReadOnlyList<SieveRule> rules, string? providerId, string? scriptName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replace the target's script with raw Sieve text. Used by the advanced editor.
    /// </summary>
    Task<Result> SaveRawScriptAsync(SieveConnection connection, string content, string? scriptName, CancellationToken cancellationToken = default);

    /// <summary>Deactivate and delete the managed script. No-op if it does not exist.</summary>
    Task<Result> DeleteAllRulesAsync(SieveConnection connection, CancellationToken cancellationToken = default);
}
