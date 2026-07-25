using System.ComponentModel.DataAnnotations;

namespace weesky.Snoopy.Microservice.Models;

public sealed class SecretChange
{
    [Required]
    [StringLength(256, MinimumLength = 8)]
    public string NewPassword { get; set; }

    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string OldPassword { get; set; }
}
