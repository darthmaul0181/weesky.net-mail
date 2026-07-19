using System.Diagnostics;

namespace weesky.Snoopy.Microservice.Models;

[DebuggerDisplay("{Name} ({Id})")]
public sealed class Domain
{
    /// <summary>
    /// Unique domain indentifier (3 chars).
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// The name of the domain.
    /// </summary>
    public string Name { get; set; }
}
