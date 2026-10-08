using System.Reflection;

namespace weesky.Scotty.Microservice.Models;

/// <summary>
/// The running build: the API's version from src/scotty.microservice/VERSION (suffixed -dev outside a release),
/// the short commit the SDK appends to the informational version after a '+', and the Docker image's
/// own number (docker/VERSION) when the service runs from that image.
/// </summary>
public sealed record ProductVersion(string Version, string? Commit, string? Image = null)
{
    public const string ImageVariable = "SCOTTY_IMAGE_VERSION";
    private const int ShortCommitLength = 7;

    public static ProductVersion Current { get; } = Parse(
        typeof(ProductVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? string.Empty,
        Environment.GetEnvironmentVariable(ImageVariable));

    public static ProductVersion Parse(string informationalVersion, string? image = null)
    {
        var parts = informationalVersion.Split('+', 2);
        var commit = parts.Length == 2 ? parts[1][..Math.Min(ShortCommitLength, parts[1].Length)] : null;

        return new ProductVersion(parts[0], commit, string.IsNullOrWhiteSpace(image) ? null : image.Trim());
    }
}
