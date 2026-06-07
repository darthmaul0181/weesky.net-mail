namespace weesky.Snoopy.Microservice.Models
{
    public enum SieveConditionOperator
    {
        Contains,
        Equals,
        Matches,
        Larger,
        Smaller,
        /// <summary>POSIX extended regex — emits <c>:regex</c> (requires "regex" extension). Weesky-only.</summary>
        Regex
    }
}
