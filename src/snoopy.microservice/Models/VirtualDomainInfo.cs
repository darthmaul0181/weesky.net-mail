namespace weesky.Snoopy.Microservice.Models;

public sealed class VirtualDomainInfo
{
    public string DomainId { get; set; }
    public string DomainName { get; set; }
    public List<OwnerInfo> Owners { get; set; } = new();
}
