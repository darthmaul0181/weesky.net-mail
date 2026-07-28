using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// A setting of the instance, not of an account: the table carries no user_id, and naming the
/// application is an administrator's decision, not a reader's preference.
///
/// Key/value for the same reason that already holds for user_preferences: without EF migrations,
/// a typed column would mean a hand-run ALTER on the server for every new setting.
/// </summary>
[Table("app_settings")]
public sealed class AppSetting
{
    /// <summary>Dotted and stable, e.g. "app.name" — never localised, never renamed lightly.</summary>
    [Column("setting_key")]
    public string SettingKey { get; set; } = string.Empty;

    /// <summary>Always a string. The registry knows how to read it back.</summary>
    [Column("setting_value")]
    public string SettingValue { get; set; } = string.Empty;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
