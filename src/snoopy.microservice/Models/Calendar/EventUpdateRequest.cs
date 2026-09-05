namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>
/// The body of PUT /api/Calendar/Events/{id}: an <see cref="EventRequest"/> plus what only an
/// update needs — which instance it targets, and the hash it was read at.
/// </summary>
public sealed class EventUpdateRequest : EventRequest
{
    public EditScope Scope { get; set; } = EditScope.All;

    /// <summary>Required for <see cref="EditScope.This"/> and <see cref="EditScope.ThisAndFollowing"/>.</summary>
    public string? InstanceId { get; set; }

    /// <summary>The <c>icsHash</c> the editor read before this write; required, so a save can never
    /// silently overwrite an event that moved since it was opened.</summary>
    public string? IfHash { get; set; }
}
