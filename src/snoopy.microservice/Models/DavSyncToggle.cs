namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// One switch per protocol; the secret behind them is shared (décision 19). Required rather than
/// positional: a body that names no state would otherwise bind to false and read as a switch-off.
/// </summary>
public sealed record DavSyncToggle
{
    public required bool Enabled { get; init; }
}
