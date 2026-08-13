namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// One ManageSieve target. SASL PLAIN sends authzid \0 authcid \0 password; every account
/// authenticates with its own credentials, so <c>AuthorizationIdentity</c> is always empty and
/// <c>AuthenticationIdentity</c> is the account itself. Built by <c>RulesController</c>, nowhere else.
/// </summary>
public sealed record SieveConnection(
    string Host, int Port, string AuthorizationIdentity, string AuthenticationIdentity, string Password)
{
    /// <summary>Redacted: the generated ToString would print the password into any log line.</summary>
    public override string ToString() =>
        $"{Host}:{Port} (authz={AuthorizationIdentity}, authc={AuthenticationIdentity})";
}
