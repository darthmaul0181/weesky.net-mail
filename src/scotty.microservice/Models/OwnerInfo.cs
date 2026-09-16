namespace weesky.Scotty.Microservice.Models;

public sealed class OwnerInfo
{
    public int OwnerId { get; set; }
    public string OwnerEmail { get; set; } = string.Empty;
}
