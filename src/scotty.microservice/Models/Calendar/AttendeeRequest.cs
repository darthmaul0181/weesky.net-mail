namespace weesky.Scotty.Microservice.Models.Calendar;

/// <summary>One entry of <c>attendees</c> in the request body.</summary>
public sealed class AttendeeRequest
{
    public string Email { get; set; } = string.Empty;

    public string? Name { get; set; }
}
