using System.Diagnostics;

namespace weesky.Snoopy.Microservice.Models;

[DebuggerDisplay("{Name} ({Id})")]
public sealed class Domain
{
    /// <summary>
    /// Unique domain indentifier (3 chars).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The name of the domain.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Aliases anchored on this domain, which deleting it takes with them. Carried on the listing
    /// because nothing in this application lets an admin enumerate another user's aliases, so a
    /// confirmation has no other way to say what the deletion costs.
    /// </summary>
    public int AliasCount { get; set; }
}
