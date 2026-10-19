using System.Text;
using CSharpFunctionalExtensions;
using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Microservice.RuleProviders;

internal static class SieveQuoting
{
    internal const string ControlCharacter =
        "A rule may not contain control characters (line breaks, tabs or NUL)";

    /// <summary>
    /// Refuses a rule carrying a control character in any value that reaches
    /// <see cref="Quote(string)"/>. Quoting escapes <c>"</c> and <c>\</c>, which is all a Sieve
    /// quoted-string needs — a CRLF inside one is legal, so nothing downstream would object, and
    /// the compiled script would carry a line break the author never sees in the rule editor.
    ///
    /// Refusing at validation is what <see cref="Services.ManageSieveSession"/> does for a script
    /// name, and for the same reason: a value that changes the shape of what goes on the wire is
    /// caught where it can still be named, not where it becomes an opaque server refusal.
    /// The two providers share this so neither can be hardened without the other.
    /// </summary>
    public static Result RejectControlCharacters(SieveRule rule)
    {
        if (HasControlCharacter(rule.Name)) return Result.Failure(ControlCharacter);

        foreach (var condition in rule.Conditions ?? [])
            if (HasControlCharacter(condition.Value) || HasControlCharacter(condition.HeaderName))
                return Result.Failure(ControlCharacter);

        foreach (var action in rule.Actions ?? [])
            if (HasControlCharacter(action.Argument)) return Result.Failure(ControlCharacter);

        return Result.Success();
    }

    private static bool HasControlCharacter(string? value) => value is not null && value.Any(char.IsControl);

    /// <summary>
    /// A Sieve size-test number (RFC 5228 §5.9): digits, optionally suffixed with K/M/G. Shared so
    /// neither provider can emit <c>size :over</c>/<c>:under</c> with a value the other refused —
    /// unvalidated, it is interpolated into the script unquoted, so anything not shaped like a
    /// number is a script-injection point, not merely a display glitch.
    /// </summary>
    public static bool IsValidSize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        var trimmed = s.Trim();
        if (trimmed.Length == 0) return false;
        var suffix = trimmed[^1];
        var digits = suffix is 'K' or 'M' or 'G' or 'k' or 'm' or 'g' ? trimmed[..^1] : trimmed;
        return long.TryParse(digits, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0;
    }

    public static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\');
            sb.Append(c);
        }
        sb.Append('"');
        return sb.ToString();
    }
}
