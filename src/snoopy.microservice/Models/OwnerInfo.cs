namespace weesky.Snoopy.Microservice.Models;

public sealed class OwnerInfo
{
    public int OwnerId { get; set; }
    public string OwnerEmail { get; set; } = string.Empty;
}
