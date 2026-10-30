using System.ComponentModel.DataAnnotations;
using System.Net.Mail;

namespace weesky.Scotty.Microservice.Models;

/// <summary>The sender whose picture is asked for: one bare address, never a display form.</summary>
public sealed record AvatarRequest([Required, StringLength(AvatarRequest.MaxLength)] string? Address)
{
    public const int MaxLength = 254;

    /// <summary>True for a lone address such as <c>bob@example.com</c>; false for <c>Bob &lt;bob@example.com&gt;</c>.</summary>
    public bool IsBareAddress =>
        Address is { Length: > 0 and <= MaxLength }
        && MailAddress.TryCreate(Address, out var parsed)
        && parsed.Address == Address.Trim();
}
