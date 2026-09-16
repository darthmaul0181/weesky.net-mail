namespace weesky.Scotty.Microservice.Models.Mail;

/// <summary>The guest who answered, what they said, whether the calendar may take it, whether it already has.</summary>
public sealed record InvitationReply(string Email, string? Name, string PartStat, ReplyStatus Status, bool Applied);
