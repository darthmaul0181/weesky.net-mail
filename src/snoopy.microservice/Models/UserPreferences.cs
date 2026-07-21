using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Models;

/// <summary>One preference the client may set, with the values it may take.</summary>
public sealed record PreferenceDefinition(string Key, string Default, IReadOnlyList<string> Allowed);

/// <summary>
/// The registry of known preferences: the one place a new setting is declared.
///
/// It is what keeps a key/value table honest. The database cannot check a key or a value, so
/// this does — an unknown key never reaches it, and a row whose value the registry no longer
/// accepts falls back to the default rather than reaching the client.
/// </summary>
public static class UserPreferences
{
    public const string MailPageSize = "mail.pageSize";
    public const string MailShowPreview = "mail.showPreview";

    private static readonly string[] Booleans = ["true", "false"];

    public static IReadOnlyList<PreferenceDefinition> All { get; } =
    [
        new(MailPageSize, "30", ["10", "20", "30", "50", "100", "all"]),
        new(MailShowPreview, "true", Booleans),
    ];

    public static bool IsValid(string key, string value) =>
        All.FirstOrDefault(p => p.Key == key)?.Allowed.Contains(value) ?? false;

    /// <summary>
    /// What the account actually gets: every default, with a stored row winning where it is
    /// still one the registry accepts.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Effective(IEnumerable<UserPreference> stored)
    {
        var effective = All.ToDictionary(p => p.Key, p => p.Default, StringComparer.Ordinal);

        foreach (var row in stored)
        {
            if (IsValid(row.PreferenceKey, row.PreferenceValue))
                effective[row.PreferenceKey] = row.PreferenceValue;
        }

        return effective;
    }
}
