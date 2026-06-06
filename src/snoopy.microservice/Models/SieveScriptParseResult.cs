namespace weesky.Snoopy.Microservice.Models
{
    /// <summary>
    /// Result of decoding a Sieve script returned by the ManageSieve server.
    /// </summary>
    public class SieveScriptParseResult
    {
        public SieveScriptKind Kind { get; init; }

        /// <summary>
        /// The structured rules decoded from the WEESKY-RULES marker. Empty when
        /// <see cref="Kind"/> is <see cref="SieveScriptKind.Advanced"/>.
        /// </summary>
        public IReadOnlyList<SieveRule> Rules { get; init; } = Array.Empty<SieveRule>();
    }
}
