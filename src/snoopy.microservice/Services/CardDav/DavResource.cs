namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// What an href resolved to. <c>DavName</c> is null for anything but a card, and <c>UserId</c> is
/// <see cref="Guid.Empty"/> on the service root, which names no user. The name comes back decoded
/// but <c>not</c> judged: "not one of our resources" (404) and "that name is not acceptable" (403)
/// are two different answers, and only the caller knows which it owes.
/// </summary>
internal sealed record DavResource(DavResourceKind Kind, Guid UserId, string? DavName);
