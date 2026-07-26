namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// Why a credential check failed. For the audit log only — brute-force triage needs to tell an
/// address spray apart from an attack on one known account.
///
/// It must never reach a response body, and the login response must not vary with it: the whole
/// point of the check that produces it is that an attacker cannot learn which value came back,
/// by content or by timing.
/// </summary>
public enum CredentialResult
{
    Ok,
    UnknownAccount,
    Deactivated,
    WrongPassword
}

/// <summary>The outcome of a credential check. <see cref="User"/> is set only when <see cref="Result"/> is Ok.</summary>
public readonly record struct CredentialCheck(CredentialResult Result, User? User)
{
    public static CredentialCheck Failed(CredentialResult result) => new(result, null);

    public static CredentialCheck Success(User user) => new(CredentialResult.Ok, user);
}
