using System.ComponentModel.DataAnnotations;

namespace weesky.Scotty.Microservice.Models;

public sealed class AdminVirtualDomainOwnerRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "A valid user id is required")]
    public int UserId { get; set; }
}
