using System.Diagnostics;
using System.Text.Json.Serialization;

namespace weesky.Snoopy.Microservice.Models;

[DebuggerDisplay("{Email}")]
public sealed class User
{
    public User(string email)
    {
        ArgumentNullException.ThrowIfNull(email);

        string[] mailParts = email.Split('@');

        if (mailParts.Length != 2)
            throw new ArgumentException($"{email} is not a valid email address", nameof(email));

        Name = mailParts[0];
        Domain = mailParts[1];
    }

    [JsonIgnore]
    public string Email => $"{Name}@{Domain}";

    /// <summary>
    /// The name of the user (the mailbox name).
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The domain in which the user (mailbox) exists.
    /// </summary>
    public string Domain { get; set; }

    /// <summary>
    /// The full name of the user.
    /// </summary>
    public string? FullName { get; set; }

    /// <summary>The snoopy_webmail surrogate key. Stamped into the JWT at login, read every request.</summary>
    public Guid WebmailUid { get; set; }
}
