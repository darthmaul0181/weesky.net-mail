using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace weesky.Snoopy.Microservice.Models;

[DebuggerDisplay("{DebuggerDisplay}")]
public sealed class Alias
{
    /// <summary>
    /// The alias name
    /// </summary>
    [Required]
    [StringLength(30, MinimumLength = 1)]
    [RegularExpression(@"^[A-Za-z0-9._%+-]+$")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The domain
    /// </summary>
    [Required]
    [StringLength(30, MinimumLength = 1)]
    public string Domain { get; set; } = string.Empty;

    [JsonIgnore]
    public string DebuggerDisplay => $"{Name}@{Domain}";

    public override bool Equals(object? obj)
    {
        if (obj == null)
            return false;


        if (obj is Alias otherAlias)
        {
            return string.Equals(Name, otherAlias.Name, StringComparison.InvariantCultureIgnoreCase)
                && string.Equals(Domain, otherAlias.Domain, StringComparison.InvariantCultureIgnoreCase);
        }

        return false;
    }

    public override int GetHashCode()
    {
        return StringComparer.InvariantCultureIgnoreCase.GetHashCode($"{Name}@{Domain}");
    }
}
