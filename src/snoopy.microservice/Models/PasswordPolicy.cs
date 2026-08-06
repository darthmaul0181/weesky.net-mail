namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// The one place the password floor is written. An admin creating an account and a user changing
/// their own password reach two different repositories, and the two must not disagree about what
/// counts as long enough.
/// </summary>
internal static class PasswordPolicy
{
    internal const int MinimumLength = 8;
}
