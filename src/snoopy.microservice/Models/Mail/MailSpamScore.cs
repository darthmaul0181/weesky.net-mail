namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>The spam filter's own verdict: score, the threshold it judges against, and the header it came from.</summary>
public sealed record MailSpamScore(double Score, double Threshold, string Raw);
