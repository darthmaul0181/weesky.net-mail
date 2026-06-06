using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories
{
    public class SieveRepository : ISieveRepository
    {
        private readonly IManageSieveClient _client;
        private readonly ISieveScriptCompiler _compiler;
        private readonly SieveOptions _options;
        private readonly ILogger<SieveRepository> _logger;

        public SieveRepository(
            IManageSieveClient client,
            ISieveScriptCompiler compiler,
            IOptions<SieveOptions> options,
            ILogger<SieveRepository> logger)
        {
            _client = client;
            _compiler = compiler;
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

            if (!list.Value.Any(e => string.Equals(e.Name, _options.ScriptName, StringComparison.Ordinal)))
            {
                return Result.Success(new SieveRuleSet { Kind = SieveScriptKind.Structured });
            }

            var script = await session.GetScriptAsync(_options.ScriptName, cancellationToken);
            if (script.IsFailure) return Result.Failure<SieveRuleSet>(script.Error);

            var parsed = _compiler.Parse(script.Value);
            return Result.Success(new SieveRuleSet
            {
                Kind = parsed.Kind,
                Rules = parsed.Rules,
                RawScript = script.Value
            });
        }

        public async Task<Result<string>> GetRawScriptAsync(User user, CancellationToken cancellationToken = default)
        {
            var ruleSet = await GetRuleSetAsync(user, cancellationToken);
            return ruleSet.IsFailure
                ? Result.Failure<string>(ruleSet.Error)
                : Result.Success(ruleSet.Value.RawScript);
        }

        public async Task<Result> SaveRulesAsync(User user, IReadOnlyList<SieveRule> rules, CancellationToken cancellationToken = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (rules == null) return Result.Failure("Rules collection is required");

            var compiled = _compiler.Compile(rules);
            if (compiled.IsFailure) return Result.Failure(compiled.Error);

            return await PutAndActivateAsync(user, compiled.Value, cancellationToken);
        }

        public Task<Result> SaveRawScriptAsync(User user, string content, CancellationToken cancellationToken = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            return PutAndActivateAsync(user, content ?? string.Empty, cancellationToken);
        }

        public async Task<Result> DeleteAllRulesAsync(User user, CancellationToken cancellationToken = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _client.OpenSessionAsync(user.Email, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
            await using var session = sessionResult.Value;

            var list = await session.ListScriptsAsync(cancellationToken);
            if (list.IsFailure) return list;

            var entry = list.Value.FirstOrDefault(e => string.Equals(e.Name, _options.ScriptName, StringComparison.Ordinal));
            if (entry == null) return Result.Success();

            if (entry.IsActive)
            {
                var deactivate = await session.SetActiveAsync(string.Empty, cancellationToken);
                if (deactivate.IsFailure) return deactivate;
            }

            return await session.DeleteScriptAsync(_options.ScriptName, cancellationToken);
        }

        private async Task<Result> PutAndActivateAsync(User user, string scriptContent, CancellationToken cancellationToken)
        {
            var sessionResult = await _client.OpenSessionAsync(user.Email, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
            await using var session = sessionResult.Value;

            var put = await session.PutScriptAsync(_options.ScriptName, scriptContent, cancellationToken);
            if (put.IsFailure)
            {
                _logger.LogWarning("PUTSCRIPT rejected for user={User}: {Error}", user.Email, put.Error);
                return put;
            }

            var activate = await session.SetActiveAsync(_options.ScriptName, cancellationToken);
            if (activate.IsFailure)
            {
                _logger.LogWarning("SETACTIVE failed for user={User}: {Error}", user.Email, activate.Error);
            }
            return activate;
        }
    }
}
