using System.ComponentModel.DataAnnotations;

namespace weesky.Snoopy.Microservice.Models
{
    /// <summary>Request body for PUT /api/Admin/users/{id} — password is optional (null = keep unchanged).</summary>
    public class AdminUserRequest
    {
        [Required]
        public string UserName { get; set; }
        public string DomainId { get; set; }
        public string? Password { get; set; }
        public string? FullName { get; set; }
        public int QuotaMb { get; set; } = 1024;
        public bool Active { get; set; } = true;
        public bool Admin { get; set; } = false;
    }

    /// <summary>Request body for POST /api/Admin/users — password is required for account creation.</summary>
    public class AdminCreateUserRequest : AdminUserRequest
    {
        [Required]
        public new string Password { get; set; }
    }
}
