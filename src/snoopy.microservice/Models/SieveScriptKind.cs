namespace weesky.Snoopy.Microservice.Models
{
    /// <summary>
    /// Indicates whether a Sieve script could be decoded into structured rules
    /// (the WEESKY-RULES marker was present and valid) or must be edited as raw text.
    /// </summary>
    public enum SieveScriptKind
    {
        Structured,
        Advanced
    }
}
