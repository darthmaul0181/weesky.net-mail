namespace weesky.Snoopy.Microservice.Models;

public sealed class AdminUserInfo
{
    public int Id { get; set; }
    public string UserName { get; set; }
    public string DomainId { get; set; }
    public string DomainName { get; set; }
    public string FullName { get; set; }
    public int QuotaMb { get; set; }
    public bool Active { get; set; }
    public bool Admin { get; set; }
    public List<LastLoginEntry> LastLogins { get; set; } = new();
}
