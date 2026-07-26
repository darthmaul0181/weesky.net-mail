namespace weesky.Snoopy.Microservice.Authentication.Models;

public sealed class TokenConstants
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpiryInMinutes { get; set; }
    public string Key { get; set; } = string.Empty;
    public string AuthCookieName { get; set; } = string.Empty;
}
