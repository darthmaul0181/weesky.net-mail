namespace weesky.Scotty.Microservice.Models;

/// <summary>What the client reads to show the credit and build the versioned image address.</summary>
public sealed record DailyImageInfo(string Version, string Title, string Copyright);
