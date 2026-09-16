using weesky.Scotty.Microservice.Models.Dav;

namespace weesky.Scotty.Microservice.Services.Calendar.Invitations;

/// <summary>What applying a REPLY's text did. Exactly one of: a refusal (<see cref="Refusal"/>, the
/// file is not a reply or nothing can be applied), <see cref="AlreadyApplied"/>, a write the
/// calendar did not take (<see cref="WriteStatus"/> other than Created/Replaced), or <see cref="Applied"/>.
/// <see cref="Context"/> is the state after the write when applied, before it otherwise.</summary>
internal sealed record IcsReplyOutcome(
    ParsedInvitation? Parsed, InvitationContext? Context, bool Applied, bool AlreadyApplied,
    DavWriteStatus? WriteStatus, string? Refusal);
