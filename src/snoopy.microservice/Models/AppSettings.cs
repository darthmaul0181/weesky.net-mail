using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// One instance setting. A non-null <paramref name="Allowed"/> enumerates the accepted values;
/// null, the value is free text bounded by <paramref name="MaxLength"/>, measured trimmed.
/// </summary>
public sealed record AppSettingDefinition(
    string Key, string Default, int MaxLength, IReadOnlyList<string>? Allowed = null);

/// <summary>
/// The registry of instance settings: the one place any of them is declared.
///
/// Same role as <see cref="UserPreferences"/> for account preferences — the database cannot
/// check a key or a value, so this does, and a row whose value the registry no longer accepts
/// falls back to the default rather than reaching the client.
/// </summary>
public static class AppSettings
{
    public const string Installable = "app.installable";
    public const string Name = "app.name";
    public const string ShortName = "app.shortName";

    private static readonly string[] Booleans = ["true", "false"];

    public static IReadOnlyList<AppSettingDefinition> All { get; } =
    [
        new(Installable, "false", 5, Booleans),
        new(Name, "Snoopy mail", 60),
        new(ShortName, "Snoopy", 12),
    ];

    public static bool IsValid(string key, string value)
    {
        var definition = Find(key);
        if (definition is null) return false;
        if (definition.Allowed is not null) return definition.Allowed.Contains(value);

        var trimmed = value.Trim();
        return trimmed.Length >= 1 && trimmed.Length <= definition.MaxLength;
    }

    /// <summary>What goes to the database. A name is trimmed; an enumerated value is already exact.</summary>
    public static string Normalize(string key, string value) =>
        Find(key)?.Allowed is null ? value.Trim() : value;

    /// <summary>Every default, a stored row winning wherever the registry still accepts it.</summary>
    public static IReadOnlyDictionary<string, string> Effective(IEnumerable<AppSetting> stored)
    {
        var effective = All.ToDictionary(s => s.Key, s => s.Default, StringComparer.Ordinal);

        foreach (var row in stored)
        {
            if (IsValid(row.SettingKey, row.SettingValue))
                effective[row.SettingKey] = Normalize(row.SettingKey, row.SettingValue);
        }

        return effective;
    }

    private static AppSettingDefinition? Find(string key) => All.FirstOrDefault(s => s.Key == key);
}
