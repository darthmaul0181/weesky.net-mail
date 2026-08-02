using System.ComponentModel.DataAnnotations;

namespace weesky.Snoopy.Microservice.Models;

public sealed class AdminDomainRequest
{
    /// <summary>
    /// No binding rule on purpose: PUT /domains/{id} binds this body but takes the id from
    /// the route, so a constraint here would refuse a legal update. The create side's 1-3
    /// character rule lives in AdminRepository.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "Domain name is required")]
    public string Name { get; set; } = string.Empty;
}
