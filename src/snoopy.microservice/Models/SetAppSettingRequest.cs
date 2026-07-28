namespace weesky.Snoopy.Microservice.Models;

/// <summary>Body of PUT /api/AppSettings. Both fields must name an entry of the registry.</summary>
public sealed class SetAppSettingRequest
{
    public string? Key { get; set; }

    public string? Value { get; set; }
}
