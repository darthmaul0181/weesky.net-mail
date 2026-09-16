using System.Reflection;

namespace weesky.Scotty.Microservice.Models;

/// <summary>
/// The running build: the product version from the VERSION file (suffixed -dev outside a release)
/// and the short commit the SDK appends to the informational version after a '+'.
/// </summary>
public sealed record ProductVersion(string Version, string? Commit)
{
    private const int ShortCommitLength = 7;

    public static ProductVersion Current { get; } = Parse(
        typeof(ProductVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? string.Empty);

    public static ProductVersion Parse(string informationalVersion)
    {
        var parts = informationalVersion.Split('+', 2);
        var commit = parts.Length == 2 ? parts[1][..Math.Min(ShortCommitLength, parts[1].Length)] : null;

        return new ProductVersion(parts[0], commit);
    }
}
