namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>The body of POST /api/ContactGroups and PUT /api/ContactGroups/{id}. Nullable at the
/// wire level so <see cref="Services.ContactValidator"/> answers the missing name, not the binder.</summary>
public sealed record ContactGroupRequest(string? Name);
