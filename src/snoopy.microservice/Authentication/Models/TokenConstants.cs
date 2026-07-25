namespace weesky.Snoopy.Microservice.Authentication.Models;

public sealed class TokenConstants
{
    public string Issuer { get; set; }
    public string Audience { get; set; }
    public int ExpiryInMinutes { get; set; }
    public string Key { get; set; }
    public string AuthCookieName { get; set; }
}
