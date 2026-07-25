namespace weesky.Snoopy.Microservice.Models;

/// <summary>Body of PUT /api/Preferences. Both fields must name entries the registry knows.</summary>
public sealed class SetPreferenceRequest
{
    public string? Key { get; set; }

    public string? Value { get; set; }
}
