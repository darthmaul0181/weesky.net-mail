namespace weesky.Scotty.Microservice.Models;

public sealed class LastLoginEntry
{
    public string Service { get; set; } = string.Empty;
    public DateTime? At { get; set; }
}
