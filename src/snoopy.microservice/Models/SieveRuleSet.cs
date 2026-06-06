namespace weesky.Snoopy.Microservice.Models
{
    /// <summary>
    /// The user-facing view of their Sieve configuration: both the decoded structured rules
    /// (when present) and the underlying raw script. Returned by the repository so the
    /// frontend can switch between structured and advanced editing without a second fetch.
    /// </summary>
    public class SieveRuleSet
    {
        public SieveScriptKind Kind { get; init; }

        public IReadOnlyList<SieveRule> Rules { get; init; } = Array.Empty<SieveRule>();

        /// <summary>
        /// The exact text returned by the ManageSieve server, or an empty string if no
        /// script has been uploaded yet.
        /// </summary>
        public string RawScript { get; init; } = string.Empty;

        /// <summary>
        /// When non-null, indicates the content was fetched from another active script
        /// (e.g. one created by Rainloop) because no managed script exists yet. On the
        /// next save, a new managed script will be created and activated, deactivating
        /// this one. The original script is not deleted.
        /// </summary>
        public string? AdoptedFromScriptName { get; init; }
    }
}
