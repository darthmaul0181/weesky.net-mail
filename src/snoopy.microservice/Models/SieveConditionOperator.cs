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
        Regex,
        /// <summary>Date comparison — emits <c>:value "lt"</c> (date fields only; requires "relational"). Weesky-only.</summary>
        Before,
        /// <summary>Date comparison — emits <c>:value "ge"</c> (date fields only; requires "relational"). Weesky-only.</summary>
        OnOrAfter
    }
}
