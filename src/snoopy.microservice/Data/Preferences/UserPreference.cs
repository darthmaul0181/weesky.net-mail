using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One preference an account has set. Key/value rather than a column per option: this project
/// has no EF migrations, so a typed column would mean a hand-run ALTER on the server for every
/// new setting. Here a new preference is a code change alone.
///
/// Absence of a row means the default in <see cref="Models.UserPreferences"/> applies, so an
/// account that never opened the settings has no rows at all.
/// </summary>
[Table("user_preferences")]
public sealed class UserPreference
{
    [Column("user_id")]
    public Guid UserId { get; set; }

    /// <summary>Dotted and stable, e.g. "mail.pageSize" — never localised, never renamed lightly.</summary>
    [Column("preference_key")]
    public string PreferenceKey { get; set; } = string.Empty;

    /// <summary>Always a string. The registry knows how to read it back.</summary>
    [Column("preference_value")]
    public string PreferenceValue { get; set; } = string.Empty;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
