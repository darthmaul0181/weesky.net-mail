using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models.WhatsNew;

namespace weesky.Scotty.Microservice.Repositories;

internal static class WhatsNewItemStamps
{
    /// <summary>Every mark means the novelty was on screen, so each one also starts the expiry.</summary>
    public static void Stamp(this WhatsNewItem item, WhatsNewMark mark, DateTime now)
    {
        item.FirstShownAt ??= now;
        if (mark == WhatsNewMark.Listed) item.ListedAt ??= now;
        if (mark == WhatsNewMark.Seen) item.SeenAt ??= now;
    }
}
