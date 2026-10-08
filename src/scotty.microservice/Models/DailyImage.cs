namespace weesky.Scotty.Microservice.Models;

/// <summary>The image of the day as the API holds it: the photo and the credit it must carry.</summary>
public sealed record DailyImage(
    string Version, string Title, string Copyright, byte[] Bytes, DateTimeOffset ExpiresAt);
