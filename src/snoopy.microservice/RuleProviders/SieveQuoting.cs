using System.Text;
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.RuleProviders;

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
