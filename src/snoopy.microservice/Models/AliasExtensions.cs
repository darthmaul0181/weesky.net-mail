namespace weesky.Snoopy.Microservice.Models;

internal static class AliasExtensions
{
    /// <summary>The one place an alias becomes an email address; identities and sending both read it.</summary>
    public static string ToAddress(this Alias alias) => $"{alias.Name}@{alias.Domain}";

    /// <summary>The account's alias addresses, in the shape the identity rules consume.</summary>
    public static List<string> ToAddresses(this IEnumerable<Alias> aliases) => aliases.Select(ToAddress).ToList();
}
