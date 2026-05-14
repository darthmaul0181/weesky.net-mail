namespace weesky.Snoopy.Microservice.Models
{
    public class AdminUserRequest
    {
        public string UserName { get; set; }
        public string DomainId { get; set; }
        public string? Password { get; set; }
        public string? FullName { get; set; }
        public int QuotaMb { get; set; } = 1024;
        public bool Active { get; set; } = true;
        public bool Admin { get; set; } = false;
    }
}
