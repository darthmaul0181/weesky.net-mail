using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Services
{
    /// <summary>
    /// Bidirectional bridge between the structured rule model and Sieve script text,
    /// with the JSON model embedded as a comment marker at the top of the script so
    /// the structured representation survives a round-trip through ManageSieve.
    /// </summary>
    public interface ISieveScriptCompiler
    {
        /// <summary>
        /// Produce a Sieve script for <paramref name="rules"/>, prefixed by the
        /// WEESKY-RULES marker that encodes the structured model. Disabled rules are
        /// preserved in the marker but not emitted as executable Sieve.
        /// </summary>
        Result<string> Compile(IReadOnlyList<SieveRule> rules);

        /// <summary>
        /// Inspect a Sieve script and return the structured rules if the marker is
        /// present and decodable. Otherwise the result is flagged as Advanced.
        /// </summary>
        SieveScriptParseResult Parse(string scriptContent);
    }
}
