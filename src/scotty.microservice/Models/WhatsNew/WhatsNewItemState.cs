namespace weesky.Scotty.Microservice.Models.WhatsNew;

/// <summary>One novelty's stamps, in UTC.</summary>
public sealed record WhatsNewItemState(string Id, DateTime? FirstShownAt, DateTime? ListedAt, DateTime? SeenAt);
