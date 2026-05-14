namespace weesky.Snoopy.Microservice.Models
{
    public class DomainOwnershipInfo
    {
        public string DomainId { get; set; }
        public string DomainName { get; set; }
        public int? OwnerId { get; set; }
        public string? OwnerEmail { get; set; }
    }
}
