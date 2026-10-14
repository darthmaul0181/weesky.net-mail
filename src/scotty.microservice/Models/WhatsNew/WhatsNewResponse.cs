using weesky.Scotty.Microservice.Repositories;

namespace weesky.Scotty.Microservice.Models.WhatsNew;

/// <summary>Answer of GET /api/WhatsNew.</summary>
public sealed record WhatsNewResponse(string? Baseline, IReadOnlyList<WhatsNewItemState> Items)
{
    public static WhatsNewResponse From(WhatsNewState state) => new(
        state.Baseline,
        [.. state.Items.Select(i => new WhatsNewItemState(i.ItemId, Utc(i.FirstShownAt), Utc(i.ListedAt), Utc(i.SeenAt)))]);

    // The column is a plain datetime: without a kind the browser would read it as local time.
    private static DateTime? Utc(DateTime? value) =>
        value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
}
