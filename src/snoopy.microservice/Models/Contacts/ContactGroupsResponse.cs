namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>Every group of the book. Wrapped like <see cref="ContactListResponse"/>, and for the
/// same reason: a later field must not change the response's shape.</summary>
public sealed record ContactGroupsResponse(IReadOnlyList<ContactGroupView> Groups);
