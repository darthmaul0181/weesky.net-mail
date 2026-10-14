using weesky.Scotty.Microservice.Data.Preferences;

namespace weesky.Scotty.Microservice.Repositories;

/// <summary>An account's baseline and the rows it has for each novelty.</summary>
public sealed record WhatsNewState(string? Baseline, IReadOnlyList<WhatsNewItem> Items);
