namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// Body of <c>PUT /api/Rules</c>: the full ordered list of rules plus optional hints
/// about which provider to compile with and which ManageSieve script to write to.
/// The frontend should echo back the <c>ProviderId</c> and <c>ScriptName</c> it received
/// from the previous <c>GET</c> so saves write to the same script they came from.
/// </summary>
public sealed class SaveRulesRequest
{
    public List<SieveRule> Rules { get; set; } = new();

    /// <summary>When null, the server's default provider is used.</summary>
    public string? ProviderId { get; set; }

    /// <summary>When null, the chosen provider's default script name is used.</summary>
    public string? ScriptName { get; set; }
}
