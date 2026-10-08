using Microsoft.Extensions.Configuration;

namespace weesky.Scotty.Microservice.Configuration;

/// <summary>
/// The web pages, served by the API itself only where <c>Frontend:Path</c> names them (the Docker
/// image). <paramref name="ApiHost"/> is the API's own host when the pages call it on another
/// address, and null when one address serves both.
/// </summary>
public sealed record FrontendSettings(string Root, string ApiBase, string? ApiHost)
{
    public static FrontendSettings? Read(IConfiguration configuration)
    {
        var configured = configuration["Frontend:Path"];
        if (string.IsNullOrWhiteSpace(configured)) return null;
        var root = Path.GetFullPath(configured);
        if (!File.Exists(Path.Combine(root, "index.html")))
            throw new InvalidOperationException($"Frontend:Path is '{root}', which holds no index.html.");

        var apiBase = configuration["Frontend:ApiBase"]?.Trim() ?? "";
        if (apiBase.Length == 0) return new FrontendSettings(root, "", null);

        if (!Uri.TryCreate(apiBase, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
        {
            throw new InvalidOperationException(
                $"Frontend:ApiBase holds '{apiBase}'. Write the API's address alone, such as " +
                "https://api.example.net, or leave it empty when one address serves both the pages and the API.");
        }

        return new FrontendSettings(root, uri.GetLeftPart(UriPartial.Authority), uri.IdnHost);
    }
}
