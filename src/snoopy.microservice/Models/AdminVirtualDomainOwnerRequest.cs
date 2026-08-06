using System.ComponentModel.DataAnnotations;

namespace weesky.Snoopy.Microservice.Models;

public sealed class AdminVirtualDomainOwnerRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "A valid user id is required")]
    public int UserId { get; set; }
}
