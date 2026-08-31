namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// What the frontend must gate on: what the platform wires (<see cref="Admin"/>,
/// <see cref="Aliases"/>, <see cref="PasswordChange"/>, <see cref="ProfileEditing"/>,
/// <see cref="StrictIdentities"/>) versus what the mail servers behind this account actually
/// support (<see cref="Quota"/>, <see cref="Rules"/>) versus what this deployment publishes
/// (<see cref="Dav"/>). The groups can disagree — a weesky deployment whose Dovecot has no
/// ManageSieve enabled still answers <c>rules: false</c>.
/// </summary>
public sealed record CapabilitiesResponse(
    string Platform,
    bool Admin,
    bool Aliases,
    bool PasswordChange,
    bool ProfileEditing,
    bool StrictIdentities,
    bool Quota,
    bool Rules,
    bool Dav);
