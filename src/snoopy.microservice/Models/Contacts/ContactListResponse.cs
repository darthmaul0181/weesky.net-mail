namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>The whole book. Wrapped in an object rather than answered as a bare array, so a later
/// field — a sync token, a count — can be added without changing the response's shape.</summary>
public sealed record ContactListResponse(IReadOnlyList<ContactView> Contacts);
