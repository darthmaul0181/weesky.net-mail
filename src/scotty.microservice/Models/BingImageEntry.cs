namespace weesky.Scotty.Microservice.Models;

/// <summary>One checked entry of Bing's archive.</summary>
internal sealed record BingImageEntry(
    string Version, string Title, string Copyright, Uri ImageUrl, DateTimeOffset ExpiresAt);
