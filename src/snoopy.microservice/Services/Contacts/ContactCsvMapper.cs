using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Services.Csv;

namespace weesky.Snoopy.Microservice.Services.Contacts;

/// <summary>
/// One CSV row understood. <paramref name="Extras"/> is keyed by the normalised header, not the
/// spelling in the file: its only reader is the vCard writer, which recognises names rather than
/// prints them.
/// </summary>
internal sealed record ContactCsvRow(
    int Line,
    string? FirstName,
    string? LastName,
    string? Nickname,
    bool IsFavorite,
    IReadOnlyList<string> Addresses,
    IReadOnlyList<string> RejectedAddresses,
    IReadOnlyList<string> OverLongFields,
    IReadOnlyDictionary<string, string> Extras);

/// <summary>
/// The header table. Knows nothing about the database: it turns a parsed file into rows, and the
/// store turns rows into contacts.
/// </summary>
internal static partial class ContactCsvMapper
{
    /// <summary>Stable code: no header row named a recognised name or e-mail column. Mapped to 400.</summary>
    internal const string NoRecognisedColumn = "csv_no_recognised_column";

    private enum Column { Unknown, FirstName, LastName, Nickname, DisplayName, Address, Favorite }

    private static readonly HashSet<string> FirstNameKeys = ["firstname", "givenname", "prénom", "prenom"];
    private static readonly HashSet<string> LastNameKeys = ["lastname", "familyname", "surname", "nom"];
    private static readonly HashSet<string> NicknameKeys = ["nickname"];
    private static readonly HashSet<string> DisplayNameKeys = ["displayname", "name", "fullname"];
    private static readonly HashSet<string> FavoriteKeys = ["favorite", "favourite"];

    private static readonly HashSet<string> AddressKeys =
    [
        "emailaddress", "email", "otheremail", "homeemail", "businessemail",
        "primaryemail", "secondaryemail",
    ];

    // Both Google's "E-mail N - Value" and our own "E-mail N Address" number their extra address
    // columns, and their count follows the book they came from — a finite list would cap what we
    // can read back from either.
    [GeneratedRegex(@"^email\d+(address|value)$")]
    private static partial Regex NumberedAddressKey();

    internal static Result<IReadOnlyList<ContactCsvRow>> Map(CsvDocument document)
    {
        var keys = document.Header.Select(Normalise).ToArray();
        var columns = keys.Select(Classify).ToArray();
        var usable = columns.Any(c =>
            c is Column.FirstName or Column.LastName or Column.Nickname or Column.DisplayName or Column.Address);
        if (!usable) return Result.Failure<IReadOnlyList<ContactCsvRow>>(NoRecognisedColumn);

        return Result.Success<IReadOnlyList<ContactCsvRow>>(
            [.. document.Rows.Select(record => MapRow(keys, columns, record))]);
    }

    private static Column Classify(string key)
    {
        if (FirstNameKeys.Contains(key)) return Column.FirstName;
        if (LastNameKeys.Contains(key)) return Column.LastName;
        if (NicknameKeys.Contains(key)) return Column.Nickname;
        if (DisplayNameKeys.Contains(key)) return Column.DisplayName;
        if (FavoriteKeys.Contains(key)) return Column.Favorite;
        if (AddressKeys.Contains(key) || NumberedAddressKey().IsMatch(key)) return Column.Address;
        return Column.Unknown;
    }

    /// <summary>
    /// Lower-cased with every separator dropped, so "E-mail 1 - Value", "e_mail_1_value" and
    /// "E-Mail 1 Value" are one key. Accents are kept — they are what tells "Prénom" apart.
    /// </summary>
    private static string Normalise(string header) =>
        new([.. header.ToLowerInvariant().Where(char.IsLetterOrDigit)]);

    private static ContactCsvRow MapRow(
        IReadOnlyList<string> keys, Column[] columns, CsvRecord record)
    {
        string? first = null, last = null, nick = null, display = null;
        var favorite = false;
        var addresses = new List<string>();
        var rejected = new List<string>();
        var overLong = new List<string>();
        var extras = new Dictionary<string, string>();
        var seen = new HashSet<string>();

        for (var i = 0; i < columns.Length; i++)
        {
            var value = i < record.Fields.Count ? record.Fields[i].Trim() : string.Empty;
            if (value.Length == 0) continue;

            switch (columns[i])
            {
                case Column.FirstName: first ??= Capped(Unescaped(value), "first name", overLong); break;
                case Column.LastName: last ??= Capped(Unescaped(value), "last name", overLong); break;
                case Column.Nickname: nick ??= Capped(Unescaped(value), "nickname", overLong); break;
                case Column.DisplayName: display ??= Unescaped(value); break;
                case Column.Favorite: favorite |= IsTruthy(value); break;
                case Column.Address:
                    if (value.Length > ContactValidator.MaxAddressLength || !ContactValidator.IsValidAddress(value))
                        rejected.Add(value);
                    else if (seen.Add(IdentityResolver.Canonical(value))) addresses.Add(value);
                    break;
                default: extras.TryAdd(keys[i], value); break;
            }
        }

        // A fallback, never a field: splitting it on a space would be guessing, and wrong on every
        // compound name. The nickname is exactly where displayNameOf looks next, capped the same way.
        nick ??= first == null && last == null ? Capped(display, "nickname", overLong) : null;

        return new ContactCsvRow(record.Line, first, last, nick, favorite, addresses, rejected, overLong, extras);
    }

    /// <summary>
    /// Undoes <see cref="ContactCsvExporter"/>'s spreadsheet guard — exactly one apostrophe, and
    /// never on an address, where one may be part of the address itself.
    /// </summary>
    private static string? Unescaped(string value) =>
        (value.StartsWith('\'') ? value[1..] : value) is { Length: > 0 } stripped ? stripped : null;

    // Dropped rather than truncated: a column shift can spill a long free-text value into a name
    // field, and storing 100 characters of that would just be quieter garbage than storing all of it.
    private static string? Capped(string? value, string field, List<string> overLong)
    {
        if (value == null || value.Length <= ContactValidator.MaxNameLength) return value;
        overLong.Add(field);
        return null;
    }

    private static bool IsTruthy(string value) =>
        value == "1"
        || value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || value.Equals("oui", StringComparison.OrdinalIgnoreCase)
        // French Excel writes VRAI for a boolean — the same environment the delimiter sniffing exists for.
        || value.Equals("vrai", StringComparison.OrdinalIgnoreCase)
        || value.Equals("x", StringComparison.OrdinalIgnoreCase);
}
