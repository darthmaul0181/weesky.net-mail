using System.ComponentModel.DataAnnotations;

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

    /// <summary>
    /// When null or empty, the chosen provider's default script name is used. A control
    /// character would split the line-oriented ManageSieve command this name is written into.
    /// </summary>
    [StringLength(128)]
    [RegularExpression(@"\A[^\x00-\x1F\x7F-\x9F]*\z",
        ErrorMessage = "Script name must not contain control characters")]
    public string? ScriptName { get; set; }
}
