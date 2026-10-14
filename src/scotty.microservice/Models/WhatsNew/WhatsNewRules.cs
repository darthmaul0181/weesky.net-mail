using System.Text.RegularExpressions;

namespace weesky.Scotty.Microservice.Models.WhatsNew;

internal static partial class WhatsNewRules
{
    public const int MaxIds = 50;

    /// <summary>The refusal for a batch of ids, or null when every id is acceptable.</summary>
    public static string? RefuseIds(IReadOnlyList<string> ids) => ids switch
    {
        { Count: 0 } => "At least one item is required",
        { Count: > MaxIds } => $"No more than {MaxIds} items at a time",
        _ when ids.Any(id => id is null || !Id().IsMatch(id)) => "Item ids are 1 to 64 lowercase letters, digits or dashes",
        _ => null
    };

    public static bool IsVersion(string? version) => version is not null && Version().IsMatch(version);

    // \z, not $: $ also matches before a final newline.
    [GeneratedRegex(@"^[a-z0-9-]{1,64}\z")]
    private static partial Regex Id();

    // [0-9], not \d: .NET's \d is any Unicode digit, which the web app's version compare cannot read.
    [GeneratedRegex(@"^[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}(-dev)?\z")]
    private static partial Regex Version();
}
