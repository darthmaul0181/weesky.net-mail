namespace weesky.Snoopy.Microservice.Models;

/// <summary>Bound from the "TrustedSenders" section of appsettings.json.</summary>
public sealed class TrustedSenderOptions
{
    /// <summary>
    /// Days an approved sender keeps its allowance without a message of theirs being opened.
    /// A year: long enough that a yearly statement still finds its sender approved.
    /// </summary>
    public int RetentionDays { get; set; } = 365;
}
