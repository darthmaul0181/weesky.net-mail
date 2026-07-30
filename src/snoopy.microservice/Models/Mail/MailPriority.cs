using System.Text.Json.Serialization;

namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// The importance a sender declared. Normal is the *absence* of any priority header, not a header
/// spelling "normal" — an ordinary message carries none.
/// The member names are pinned because Program.cs registers a bare JsonStringEnumConverter, which
/// would otherwise put "High" on a wire whose every other field is camelCase.
/// </summary>
public enum MailPriority
{
    [JsonStringEnumMemberName("normal")] Normal,
    [JsonStringEnumMemberName("high")] High,
    [JsonStringEnumMemberName("low")] Low
}
