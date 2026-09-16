using System.ComponentModel.DataAnnotations;

namespace weesky.Scotty.Microservice.Models;

public sealed class FullNameChange
{
    [StringLength(255)]
    public string FullName { get; set; } = string.Empty;
}
