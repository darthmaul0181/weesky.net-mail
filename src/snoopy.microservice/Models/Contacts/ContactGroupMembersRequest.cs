namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>The body of POST and DELETE /api/ContactGroups/{id}/Members. Empty and over-cap are
/// both refused; an id the book does not hold is skipped in silence.</summary>
public sealed record ContactGroupMembersRequest(IReadOnlyList<Guid>? ContactIds);
