namespace weesky.Snoopy.Microservice.Services
{
    /// <summary>
    /// One entry in the LISTSCRIPTS response from a ManageSieve server.
    /// </summary>
    public record SieveScriptListEntry(string Name, bool IsActive);
}
