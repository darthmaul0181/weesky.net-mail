namespace weesky.Snoopy.Microservice.Models.Dav;

/// <summary>
/// The CalDAV switch. <see cref="Enabled"/> is required for the reason
/// <see cref="DavSyncToggle"/>'s is: a body naming no state would bind to false and read as a
/// switch-off. <see cref="TimeZone"/> is the browser's IANA zone, mandatory on the way on — it is
/// what the default calendar is born with — and ignored on the way off.
/// </summary>
public sealed record DavCalDavToggle
{
    public required bool Enabled { get; init; }

    public string? TimeZone { get; init; }
}
