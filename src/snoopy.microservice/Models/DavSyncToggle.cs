namespace weesky.Snoopy.Microservice.Models;

/// <summary>One switch per protocol; the secret behind them is shared (décision 19).</summary>
public sealed record DavSyncToggle(bool Enabled);
