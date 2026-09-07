namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>
/// What an href resolved to. <c>DavName</c> is null for anything but a card or an event, and
/// <c>UserId</c> is <see cref="Guid.Empty"/> on the shapes that name no user. <c>CollectionName</c>
/// is the calendar's URL segment, null off the calendar tree — last and defaulted, so the three
/// positional arguments every caller already writes keep their place. Both names come back decoded
/// but <c>not</c> judged: "not one of our resources" (404) and "that name is not acceptable" (403)
/// are two different answers, and only the caller knows which it owes.
/// </summary>
internal sealed record DavResource(
    DavResourceKind Kind, Guid UserId, string? DavName, string? CollectionName = null);
