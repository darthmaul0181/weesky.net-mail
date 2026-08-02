using System.ComponentModel.DataAnnotations;

namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>What PrepareQuote acts on: the message, and the intent the composer opens with.</summary>
public sealed record PrepareQuoteRequest
{
    [Required(ErrorMessage = "A folder is required")]
    public string Folder { get; init; } = string.Empty;

    public uint Uid { get; init; }

    /// <summary>
    /// "reply", "forward" or "editAsNew". The attribute only refuses an absent value; the
    /// exact-literal match stays in the controller, so the rule is written once.
    /// </summary>
    [Required(ErrorMessage = "Purpose must be reply, forward or editAsNew")]
    public string Purpose { get; init; } = string.Empty;
}
