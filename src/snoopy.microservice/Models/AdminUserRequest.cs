using System.ComponentModel.DataAnnotations;

namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// Request body for POST and PUT /api/Admin/users.
/// Password is optional on PUT (null = keep unchanged); the controller enforces it on POST.
/// </summary>
public sealed class AdminUserRequest
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
