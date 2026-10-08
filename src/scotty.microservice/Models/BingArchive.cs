using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;

namespace weesky.Scotty.Microservice.Models;

/// <summary>
/// Bing's image archive. It has no official API, so every field is checked, and the image is
/// fetched from an address rebuilt on a fixed host, never from one the answer supplies.
/// </summary>
internal static class BingArchive
{
    public const string Host = "https://www.bing.com";
    public const int MaxMetadataBytes = 64 * 1024;
    public const int MaxImageBytes = 5 * 1024 * 1024;

    // An archive already days old would otherwise be born expired and refetched on every visit.
    private static readonly TimeSpan MinFreshness = TimeSpan.FromHours(1);

    private static readonly Regex UrlBase = new(
        @"^/th\?id=[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Hash = new(
        "^[A-Za-z0-9]{1,64}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string MarketOf(string? lang) => lang == "fr" ? "fr-FR" : "en-US";

    public static Uri MetadataUrl(string market) =>
        new($"{Host}/HPImageArchive.aspx?format=js&idx=0&n=1&mkt={market}");

    public static Result<BingImageEntry> Parse(string json, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("images", out var images)
                || images.ValueKind != JsonValueKind.Array
                || images.GetArrayLength() == 0
                || images[0].ValueKind != JsonValueKind.Object)
                return Result.Failure<BingImageEntry>("No image in the archive");

            var image = images[0];
            var urlBase = Text(image, "urlbase");
            var hash = Text(image, "hsh");
            var title = Text(image, "title");
            var copyright = Text(image, "copyright");

            if (!UrlBase.IsMatch(urlBase)) return Result.Failure<BingImageEntry>("Unexpected image path");
            if (!Hash.IsMatch(hash)) return Result.Failure<BingImageEntry>("Unexpected image hash");
            if (title.Length is 0 or > 200 || copyright.Length is 0 or > 400)
                return Result.Failure<BingImageEntry>("Missing credit");
            if (!DateTimeOffset.TryParseExact(Text(image, "fullstartdate"), "yyyyMMddHHmm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start))
                return Result.Failure<BingImageEntry>("Unreadable start date");

            var expires = start.AddDays(1);
            if (expires < now + MinFreshness) expires = now + MinFreshness;

            return new BingImageEntry(hash, title, copyright, new Uri($"{Host}{urlBase}_1920x1080.jpg"), expires);
        }
        catch (JsonException)
        {
            return Result.Failure<BingImageEntry>("Unreadable archive");
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!.Trim()
            : string.Empty;
}
