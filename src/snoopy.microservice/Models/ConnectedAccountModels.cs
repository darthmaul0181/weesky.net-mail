using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// One attached mailbox as the settings page sees it. Carries no cipher and no password: the
/// stored secret has no shape in which it may leave this service.
///
/// <c>DomainId</c> is null for a local shared mailbox. <c>CredentialsValid</c> says whether the
/// stored cipher still opens under the session key — a local decrypt, never a connection, since
/// one IMAP dialogue per row would make this page take seconds.
/// </summary>
public sealed record ConnectedAccountResponse(
    Guid Id, string Email, string DisplayName, Guid? DomainId, string? DomainName,
    bool SieveSupported, bool CredentialsValid, DateTime CreationDate, MailAuthMode AuthMode);

/// <summary>
/// Attaching a mailbox. No host, port or security field exists here by design — endpoints come
/// from appsettings or from the admin-curated domain row, never from the caller.
/// </summary>
public sealed record ConnectAccountRequest(Guid? DomainId, string Email, string Password)
{
    /// <summary>Redacted: the generated ToString would print the password into any log line.</summary>
    public override string ToString() => $"ConnectAccountRequest ({Email}, domain={DomainId})";
}

/// <summary>Re-entering the password of an already attached mailbox.</summary>
public sealed record ConnectedAccountPasswordRequest(string Password)
{
    /// <summary>Redacted: the generated ToString would print the password into any log line.</summary>
    public override string ToString() => "ConnectedAccountPasswordRequest";
}

/// <summary>
/// An external domain in the connect form's choice list. Names and ids only: hosts, ports and
/// transport security are administrator information.
/// </summary>
public sealed record ExternalDomainChoice(Guid Id, string Name, MailAuthMode AuthMode);
