using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.RuleProviders;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class SieveRepository : ISieveRepository
{
    private readonly IManageSieveClient _client;
    private readonly IRuleProviderRegistry _registry;
    private readonly ILogger<SieveRepository> _logger;

    public SieveRepository(
        IManageSieveClient client,
        IRuleProviderRegistry registry,
        ILogger<SieveRepository> logger)
    {
        _client = client;
        _registry = registry;
        _logger = logger;
    }

    public async Task<Result<SieveRuleSet>> GetRuleSetAsync(SieveConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var sessionResult = await _client.OpenSessionAsync(connection, cancellationToken);
        if (sessionResult.IsFailure) return Result.Failure<SieveRuleSet>(sessionResult.Error);
        await using var session = sessionResult.Value;

        var list = await session.ListScriptsAsync(cancellationToken);
        if (list.IsFailure) return Result.Failure<SieveRuleSet>(list.Error);

        var scriptName = ResolveScriptName(list.Value);
        if (scriptName == null)
        {
            // No script exists at all → return an empty structured set on the provider a
            // brand-new account starts on (Rainloop), so the webmail stays in sync by default.
            return Result.Success(new SieveRuleSet
            {
                Kind = SieveScriptKind.Structured,
                ProviderId = _registry.NewAccountDefault.Id,
                ScriptName = _registry.NewAccountDefault.DefaultScriptName
            });
        }

        var script = await session.GetScriptAsync(scriptName, cancellationToken);
        if (script.IsFailure) return Result.Failure<SieveRuleSet>(script.Error);

        return DecodeScript(script.Value, scriptName);
    }

    public async Task<Result<string>> GetRawScriptAsync(SieveConnection connection, CancellationToken cancellationToken = default)
    {
        var ruleSet = await GetRuleSetAsync(connection, cancellationToken);
        return ruleSet.IsFailure
            ? Result.Failure<string>(ruleSet.Error)
            : Result.Success(ruleSet.Value.RawScript);
    }

    public async Task<Result> SaveRulesAsync(SieveConnection connection, IReadOnlyList<SieveRule> rules, string? providerId, string? scriptName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (rules == null) return Result.Failure("Rules collection is required");

        var provider = providerId != null
            ? _registry.GetById(providerId)
            : _registry.Default;
        if (provider == null) return Result.Failure($"Unknown rule provider: {providerId}");

        var compiled = provider.Compile(rules);
        if (compiled.IsFailure) return Result.Failure(compiled.Error);

        var targetName = string.IsNullOrEmpty(scriptName) ? provider.DefaultScriptName : scriptName;

        var sessionResult = await _client.OpenSessionAsync(connection, cancellationToken);
        if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
        await using var session = sessionResult.Value;

        var put = await session.PutScriptAsync(targetName, compiled.Value, cancellationToken);
        if (put.IsFailure)
        {
            _logger.LogWarning("PUTSCRIPT rejected for {Connection} script={Script}: {Error}", connection, targetName, put.Error);
            return put;
        }

        var activate = await session.SetActiveAsync(targetName, cancellationToken);
        if (activate.IsFailure)
        {
            _logger.LogWarning("SETACTIVE failed for {Connection} script={Script}: {Error}", connection, targetName, activate.Error);
            return activate;
        }

        // Drop any other provider's managed script so only one stays on the server and the
        // next load is unambiguous. Best-effort: a failure here doesn't undo the save.
        await CleanupOtherManagedScriptsAsync(session, targetName, cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Delete the default-named scripts of providers other than the one we just wrote,
    /// leaving the active managed script as the single source of truth. Never touches the
    /// active script or any unrecognised (Advanced) script.
    /// </summary>
    private async Task CleanupOtherManagedScriptsAsync(IManageSieveSession session, string keepName, CancellationToken cancellationToken)
    {
        var list = await session.ListScriptsAsync(cancellationToken);
        if (list.IsFailure) return;

        var managedNames = _registry.All
            .Select(p => p.DefaultScriptName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entry in list.Value)
        {
            if (string.Equals(entry.Name, keepName, StringComparison.Ordinal)) continue;
            if (entry.IsActive) continue;                 // never remove the active script
            if (!managedNames.Contains(entry.Name)) continue; // never remove Advanced/unknown scripts

            var del = await session.DeleteScriptAsync(entry.Name, cancellationToken);
            if (del.IsFailure)
                _logger.LogWarning("Failed to delete superseded script {Script}: {Error}", entry.Name, del.Error);
        }
    }

    public Task<Result> SaveRawScriptAsync(SieveConnection connection, string content, string? scriptName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var targetName = string.IsNullOrEmpty(scriptName) ? _registry.Default.DefaultScriptName : scriptName;
        return PutAndActivateAsync(connection, targetName, content ?? string.Empty, cancellationToken);
    }

    public async Task<Result> DeleteAllRulesAsync(SieveConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var sessionResult = await _client.OpenSessionAsync(connection, cancellationToken);
        if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
        await using var session = sessionResult.Value;

        var list = await session.ListScriptsAsync(cancellationToken);
        if (list.IsFailure) return list;

        var scriptName = ResolveScriptName(list.Value);
        if (scriptName == null) return Result.Success();

        var entry = list.Value.First(e => string.Equals(e.Name, scriptName, StringComparison.Ordinal));
        if (entry.IsActive)
        {
            var deactivate = await session.SetActiveAsync(string.Empty, cancellationToken);
            if (deactivate.IsFailure) return deactivate;
        }

        return await session.DeleteScriptAsync(scriptName, cancellationToken);
    }

    /// <summary>
    /// Pick the ManageSieve script we should operate on:
    /// 1. A script whose name matches any provider's default name (prefer the active one).
    /// 2. Otherwise the currently active script (will be treated as Advanced if no provider matches).
    /// 3. Otherwise null (no script at all).
    /// </summary>
    private string? ResolveScriptName(IReadOnlyList<SieveScriptListEntry> scripts)
    {
        if (scripts.Count == 0) return null;

        foreach (var provider in _registry.All)
        {
            var match = scripts.FirstOrDefault(e => string.Equals(e.Name, provider.DefaultScriptName, StringComparison.Ordinal));
            if (match != null) return match.Name;
        }

        var active = scripts.FirstOrDefault(e => e.IsActive);
        return active?.Name;
    }

    private SieveRuleSet DecodeScript(string scriptContent, string scriptName)
    {
        var provider = _registry.Detect(scriptContent);
        if (provider == null)
        {
            return new SieveRuleSet
            {
                Kind = SieveScriptKind.Advanced,
                RawScript = scriptContent,
                ScriptName = scriptName
            };
        }

        var parsed = provider.Parse(scriptContent);
        if (parsed.IsFailure)
        {
            _logger.LogWarning("Provider {Provider} failed to parse script {Script}: {Error}", provider.Id, scriptName, parsed.Error);
            return new SieveRuleSet
            {
                Kind = SieveScriptKind.Advanced,
                RawScript = scriptContent,
                ProviderId = provider.Id,
                ScriptName = scriptName
            };
        }

        return new SieveRuleSet
        {
            Kind = SieveScriptKind.Structured,
            Rules = parsed.Value,
            RawScript = scriptContent,
            ProviderId = provider.Id,
            ScriptName = scriptName
        };
    }

    private async Task<Result> PutAndActivateAsync(SieveConnection connection, string scriptName, string scriptContent, CancellationToken cancellationToken)
    {
        var sessionResult = await _client.OpenSessionAsync(connection, cancellationToken);
        if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
        await using var session = sessionResult.Value;

        var put = await session.PutScriptAsync(scriptName, scriptContent, cancellationToken);
        if (put.IsFailure)
        {
            _logger.LogWarning("PUTSCRIPT rejected for {Connection} script={Script}: {Error}", connection, scriptName, put.Error);
            return put;
        }

        var activate = await session.SetActiveAsync(scriptName, cancellationToken);
        if (activate.IsFailure)
        {
            _logger.LogWarning("SETACTIVE failed for {Connection} script={Script}: {Error}", connection, scriptName, activate.Error);
        }
        return activate;
    }
}
