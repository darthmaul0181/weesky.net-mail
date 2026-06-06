namespace weesky.Snoopy.Microservice.Models
{
    /// <summary>
    /// Wrapper used by the raw Sieve script endpoints (<c>GET/PUT /api/Rules/raw</c>) so the
    /// payload remains a JSON object rather than a bare string.
    /// </summary>
    public class SieveRawScript
    {
        public string Content { get; set; } = string.Empty;
    }
}
