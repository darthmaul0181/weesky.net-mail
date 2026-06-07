namespace weesky.Snoopy.Microservice.RuleProviders
{
    /// <summary>
    /// Look-up service for the configured rule providers.
    /// </summary>
    public interface IRuleProviderRegistry
    {
        /// <summary>Provider used when no existing script is found and no explicit choice is made.</summary>
        IRuleProvider Default { get; }

        /// <summary>
        /// Provider a brand-new account starts on (no script yet). Rainloop, so the Snappymail
        /// webmail sees the rules from day one; users opt into the extended Weesky format later.
        /// </summary>
        IRuleProvider NewAccountDefault { get; }

        IReadOnlyList<IRuleProvider> All { get; }

        /// <summary>Resolve a provider by its identifier; returns null if unknown.</summary>
        IRuleProvider? GetById(string id);

        /// <summary>
        /// First provider that recognises the supplied script content via <see cref="IRuleProvider.CanHandle"/>,
        /// or null if no provider matches (Advanced/raw mode).
        /// </summary>
        IRuleProvider? Detect(string scriptContent);
    }
}
