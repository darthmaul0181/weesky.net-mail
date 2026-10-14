namespace weesky.Scotty.Microservice.Models.WhatsNew;

/// <summary>Body of PUT /api/WhatsNew/items.</summary>
public sealed record MarkWhatsNewRequest(IReadOnlyList<string>? Ids, WhatsNewMark? State);
