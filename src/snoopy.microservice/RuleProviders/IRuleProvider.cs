using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.RuleProviders
{
    /// <summary>
    /// A bidirectional bridge between the common structured rule model and a specific
    /// Sieve script "dialect" (a convention for embedding rule metadata as comments).
    /// Each supported dialect (e.g. weesky, rainloop) is exposed as its own provider so
    /// the microservice can interop with other clients without forcing a migration.
    /// </summary>
    public interface IRuleProvider
    {
        /// <summary>Stable identifier surfaced to API consumers.</summary>
        string Id { get; }

        /// <summary>Human-readable label for UI selectors.</summary>
        string DisplayName { get; }

        /// <summary>Default ManageSieve script name when no existing script is found.</summary>
        string DefaultScriptName { get; }

        /// <summary>
        /// Returns true if this provider recognises the supplied script content as
        /// being in its own dialect (typically by checking a marker comment).
        /// </summary>
        bool CanHandle(string scriptContent);

        /// <summary>
        /// Decode a script known to be in this provider's dialect into structured rules.
        /// Should be called only after <see cref="CanHandle"/> returns true.
        /// </summary>
        Result<IReadOnlyList<SieveRule>> Parse(string scriptContent);

        /// <summary>Emit a script in this provider's dialect.</summary>
        Result<string> Compile(IReadOnlyList<SieveRule> rules);

        /// <summary>
        /// Checks whether this provider's on-disk format can losslessly represent the rule.
        /// Returns success when it can, or a failure whose error explains why it cannot.
        /// Pure, network-free check used to preview a provider switch before committing it.
        /// Implementations must use a strict whitelist (reject anything not explicitly
        /// supported) so that future rule features fail safe rather than being silently dropped.
        /// </summary>
        Result CanRepresent(SieveRule rule);
    }
}
