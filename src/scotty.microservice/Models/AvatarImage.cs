namespace weesky.Scotty.Microservice.Models;

/// <summary>A sender's picture as fetched: the bytes and the image type they were served as.</summary>
public sealed record AvatarImage(byte[] Bytes, string ContentType);
