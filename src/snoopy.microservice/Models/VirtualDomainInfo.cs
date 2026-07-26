namespace weesky.Snoopy.Microservice.Models;

public sealed class VirtualDomainInfo
{
    public string DomainId { get; set; } = string.Empty;
    public string DomainName { get; set; } = string.Empty;
    public List<OwnerInfo> Owners { get; set; } = new();
}
