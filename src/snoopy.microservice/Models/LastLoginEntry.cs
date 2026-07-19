namespace weesky.Snoopy.Microservice.Models;

public sealed class LastLoginEntry
{
    public string Service { get; set; }
    public DateTime? At { get; set; }
}
