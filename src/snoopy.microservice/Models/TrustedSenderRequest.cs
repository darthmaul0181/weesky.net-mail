using System.ComponentModel.DataAnnotations;

namespace weesky.Snoopy.Microservice.Models;

/// <summary>One sender to start trusting. Stored canonical, so casing here is immaterial.</summary>
public sealed record TrustedSenderRequest
{
    [Required]
    [StringLength(320, MinimumLength = 3)]
    public string Address { get; init; } = string.Empty;
}
