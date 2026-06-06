using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.RuleProviders;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories
{
    public class SieveRepository : ISieveRepository
    {
        private readonly IManageSieveClient _client;
        private readonly IRuleProviderRegistry _registry;
        private readonly SieveOptions _options;
        private readonly ILogger<SieveRepository> _logger;

        public SieveRepository(
            IManageSieveClient client,
            IRuleProviderRegistry registry,
            IOptions<SieveOptions> options,
            ILogger<SieveRepository> logger)
        {
            _client = client;
            _registry = registry;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<Result<SieveRuleSet>> GetRuleSetAsync(User user, CancellationToken cancellationToken = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _client.OpenSessionAsync(user.Email, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure<SieveRuleSet>(sessionResult.Error);
            await using var session = sessionResult.Value;

            var list = await session.ListScriptsAsync(cancellationToken);
            if (list.IsFailure) return Result.Failure<SieveRuleSet>(list.Error);

            var scriptName = ResolveScriptName(list.Value);
            if (scriptName == null)
            {
                // No script exists at all → return an empty structured set using the default provider.
                return Result.Success(new SieveRuleSet
                {
                    Kind = SieveScriptKind.Structured,
                    ProviderId = _registry.Default.Id,
                    ScriptName = _registry.Default.DefaultScriptName
                });
            }

            var script = await session.GetScriptAsync(scriptName, cancellationToken);
            if (script.IsFailure) return Result.Failure<SieveRuleSet>(script.Error);

            return DecodeScript(script.Value, scriptName);
        }

        public async Task<Result<string>> GetRawScriptAsync(User user, CancellationToken cancellationToken = default)
        {
            var ruleSet = await GetRuleSetAsync(user, cancellationToken);
            return ruleSet.IsFailure
                ? Result.Failure<string>(ruleSet.Error)
                : Result.Success(ruleSet.Value.RawScript);
        }

        public async Task<Result> SaveRulesAsync(User user, IReadOnlyList<SieveRule> rules, string? providerId, string? scriptName, CancellationToken cancellationToken = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (rules == null) return Result.Failure("Rules collection is required");

            var provider = providerId != null
                ? _registry.GetById(providerId)
                : _registry.Default;
            if (provider == null) return Result.Failure($"Unknown rule provider: {providerId}");

            var compiled = provider.Compile(rules);
            if (compiled.IsFailure) return Result.Failure(compiled.Error);

            var targetName = string.IsNullOrEmpty(scriptName) ? provider.DefaultScriptName : scriptName;
            return await PutAndActivateAsync(user, targetName, compiled.Value, cancellationToken);
        }

        public Task<Result> SaveRawScriptAsync(User user, string content, string? scriptName, CancellationToken cancellationToken = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            var targetName = string.IsNullOrEmpty(scriptName) ? _registry.Default.DefaultScriptName : scriptName;
            return PutAndActivateAsync(user, targetName, content ?? string.Empty, cancellationToken);
        }

        public async Task<Result> DeleteAllRulesAsync(User user, CancellationToken cancellationToken = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _client.OpenSessionAsync(user.Email, cancellationToken);
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

        private async Task<Result> PutAndActivateAsync(User user, string scriptName, string scriptContent, CancellationToken cancellationToken)
        {
            var sessionResult = await _client.OpenSessionAsync(user.Email, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
            await using var session = sessionResult.Value;

            var put = await session.PutScriptAsync(scriptName, scriptContent, cancellationToken);
            if (put.IsFailure)
            {
                _logger.LogWarning("PUTSCRIPT rejected for user={User} script={Script}: {Error}", user.Email, scriptName, put.Error);
                return put;
            }

            var activate = await session.SetActiveAsync(scriptName, cancellationToken);
            if (activate.IsFailure)
            {
                _logger.LogWarning("SETACTIVE failed for user={User} script={Script}: {Error}", user.Email, scriptName, activate.Error);
            }
            return activate;
        }
    }
}
