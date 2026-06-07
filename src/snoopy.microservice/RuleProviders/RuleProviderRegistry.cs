namespace weesky.Snoopy.Microservice.RuleProviders
{
    public class RuleProviderRegistry : IRuleProviderRegistry
    {
        public const string DefaultProviderId = "weesky";
        public const string NewAccountProviderId = "rainloop";

        private readonly Dictionary<string, IRuleProvider> _byId;

        public RuleProviderRegistry(IEnumerable<IRuleProvider> providers)
        {
            All = providers.ToList();
            if (All.Count == 0)
                throw new InvalidOperationException("At least one IRuleProvider must be registered");

            _byId = All.ToDictionary(p => p.Id, StringComparer.Ordinal);

            Default = _byId.TryGetValue(DefaultProviderId, out var weesky)
                ? weesky
                : All[0];

            NewAccountDefault = _byId.TryGetValue(NewAccountProviderId, out var rainloop)
                ? rainloop
                : Default;
        }

        public IRuleProvider Default { get; }

        public IRuleProvider NewAccountDefault { get; }

        public IReadOnlyList<IRuleProvider> All { get; }

        public IRuleProvider? GetById(string id) =>
            string.IsNullOrEmpty(id) ? null : _byId.GetValueOrDefault(id);

        public IRuleProvider? Detect(string scriptContent)
        {
            if (string.IsNullOrEmpty(scriptContent)) return null;
            foreach (var provider in All)
            {
                if (provider.CanHandle(scriptContent)) return provider;
            }
            return null;
        }
    }
}
