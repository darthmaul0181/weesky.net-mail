namespace weesky.Snoopy.Microservice.Models;

public enum SieveConditionOperator
{
    Contains,
    /// <summary>Emits <c>not … :contains</c>. Weesky-only (Rainloop format cannot represent negation).</summary>
    NotContains,
    Equals,
    /// <summary>Emits <c>not … :is</c>. Weesky-only (Rainloop format cannot represent negation).</summary>
    NotEquals,
    Matches,
    Larger,
    Smaller,
    /// <summary>POSIX extended regex — emits <c>:regex</c> (requires "regex" extension).</summary>
    Regex,
    /// <summary>Date comparison — emits <c>:value "lt"</c> (date fields only; requires "relational"). Weesky-only.</summary>
    Before,
    /// <summary>Date comparison — emits <c>:value "ge"</c> (date fields only; requires "relational"). Weesky-only.</summary>
    OnOrAfter
}
