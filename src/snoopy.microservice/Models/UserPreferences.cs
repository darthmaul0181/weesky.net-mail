using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// One preference the client may set, with the values it may take.
///
/// <paramref name="IsSet"/> reads <paramref name="Allowed"/> as the members a comma-separated
/// subset may draw from rather than as the whole values the key accepts: four choosable icons
/// are sixteen combinations, and enumerating those would hide the rule inside its own expansion.
/// </summary>
public sealed record PreferenceDefinition(
    string Key, string Default, IReadOnlyList<string> Allowed, bool IsSet = false);

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
    public const string MailAlwaysShowImages = "mail.alwaysShowImages";
    public const string MailNotifySound = "mail.notifySound";
    public const string MailNotifyDesktop = "mail.notifyDesktop";
    public const string MailShowSpamScore = "mail.showSpamScore";
    public const string MailReadingPane = "mail.readingPane";
    public const string MailRowActions = "mail.rowActions";

    // contacts., not mail.: the preference governs a write to the address book. The trigger is a
    // send; the effect is what names the key.
    public const string ContactsCaptureRecipients = "contacts.captureRecipients";

    // mail., by the same rule read the other way: the trigger is being in the book, the effect is
    // how the mail reader treats remote images.
    public const string MailTrustContacts = "mail.trustContacts";

    private static readonly string[] Booleans = ["true", "false"];

    public static IReadOnlyList<PreferenceDefinition> All { get; } =
    [
        new(MailPageSize, "all", ["10", "20", "30", "50", "100", "all"]),
        new(MailShowPreview, "true", Booleans),
        new(MailAlwaysShowImages, "false", Booleans),
        new(MailNotifySound, "false", Booleans),
        new(MailNotifyDesktop, "false", Booleans),
        new(MailShowSpamScore, "true", Booleans),
        new(MailReadingPane, "right", ["right", "bottom", "none"]),
        // The default is the three icons the row carried before it was choosable, so an account
        // that never touches the setting sees no change at all.
        new(MailRowActions, "seen,archive,delete", ["seen", "archive", "junk", "delete"], IsSet: true),
        new(ContactsCaptureRecipients, "true", Booleans),
        new(MailTrustContacts, "false", Booleans),
    ];

    public static bool IsValid(string key, string value)
    {
        var definition = All.FirstOrDefault(p => p.Key == key);
        if (definition is null)
            return false;

        return definition.IsSet ? IsValidSubset(definition, value) : definition.Allowed.Contains(value);
    }

    /// <summary>
    /// Empty is a real answer — every icon switched off — so it is the one value accepted without
    /// a member. A repeated member is refused rather than folded: it is a set, and tolerating it
    /// would make the stored string and what the client renders two different things.
    /// </summary>
    private static bool IsValidSubset(PreferenceDefinition definition, string value)
    {
        if (value.Length == 0)
            return true;

        var members = value.Split(',');

        return members.Distinct(StringComparer.Ordinal).Count() == members.Length
            && members.All(member => definition.Allowed.Contains(member));
    }

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
