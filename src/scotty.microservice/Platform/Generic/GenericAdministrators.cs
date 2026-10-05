using Microsoft.Extensions.Configuration;

namespace weesky.Scotty.Microservice.Platform.Generic;

/// <summary>
/// Who administers a generic deployment: no directory holds an admin flag, so the operator names
/// the addresses. Read once at startup; a misspelt entry refuses to start rather than silently
/// granting nobody.
/// </summary>
public sealed class GenericAdministrators(IEnumerable<string> emails)
{
    public const string ConfigurationKey = "Generic:Administrators";

    private readonly HashSet<string> _emails = new(emails, StringComparer.OrdinalIgnoreCase);

    public bool Contains(string email) => _emails.Contains(email);

    public static GenericAdministrators From(IConfiguration configuration)
    {
        var entries = (configuration[ConfigurationKey] ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
            if (entry.Split('@') is not [{ Length: > 0 }, { Length: > 0 }])
                throw new InvalidOperationException(
                    $"'{ConfigurationKey}' holds '{entry}', which is not an email address.");

        return new GenericAdministrators(entries);
    }
}
