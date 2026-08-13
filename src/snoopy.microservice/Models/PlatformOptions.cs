namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// Which platform hosts this deployment, read from the root <c>Platform</c> key. There is no
/// default: a deployment that does not say refuses to start, because guessing either way is a
/// silent answer to "does this service administer the mailboxes it serves".
/// </summary>
public sealed class PlatformOptions
{
    /// <summary>weesky.net: the dovecot database answers for accounts, aliases and admin rights.</summary>
    public const string Weesky = "weesky";

    /// <summary>Any IMAP server: nothing behind the mailbox, so no directory to administer.</summary>
    public const string Generic = "generic";

    public string? Platform { get; set; }

    public bool IsWeesky => Platform == Weesky;
}
