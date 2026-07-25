namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>What PrepareQuote acts on: the message, and the intent the composer opens with.</summary>
public sealed record PrepareQuoteRequest
{
    public string Folder { get; init; } = string.Empty;
    public uint Uid { get; init; }

    /// <summary>"reply", "forward" or "editAsNew".</summary>
    public string Purpose { get; init; } = string.Empty;
}
