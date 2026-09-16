using MimeKit;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Models.Dav;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services.Calendar.Invitations;
using weesky.Scotty.Microservice.Services.Dav;

namespace weesky.Scotty.Microservice.Services.Calendar.Scheduling;

/// <summary>
/// Decides from the file before and the file after, advances a SEQUENCE a client left behind by one
/// more textual write, composes, hands the mails to the user's session or to the service queue, and
/// stamps the two columns (décisions 9 and 10). Credentials never reach it: the door passes a session opener.
/// The door's write has committed before it runs, so all of it runs under no token but the user's own SMTP
/// send, and a mail that send can no longer carry goes by the queue: a client hanging up must neither lose
/// a CANCEL nothing would catch up nor fail a write that succeeded.
/// </summary>
internal sealed class InvitationScheduler(
    ICalendarEventStore store, IDavCalendarWriter writer, IUserAddresses addresses, IOrganizerIdentity organizer,
    IMailSender sender, IServiceMailQueue queue, IUserPreferenceStore preferences, TimeProvider clock,
    ILogger<InvitationScheduler> logger) : IInvitationScheduler
{
    private static readonly SchedulingReport Nothing = new(null, 0);

    public async Task<SchedulingReport> AfterWriteAsync(
        User user, EventChange change, WriteOrigin origin,
        Func<CancellationToken, Task<MailAccountConnection?>>? session, string? language, CancellationToken cancellationToken)
    {
        if (NothingToSchedule(change, origin)) return Nothing;
        try
        {
            return await RunAsync(user, change, origin, session, language, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scheduling failed after the write of {DavName}; invitees not told", change.DavName);
            return new SchedulingReport(change.Before?.SchedulingOwner, 0);
        }
    }

    /// <summary>What the decider would answer without reading anything: an event nobody owns is only
    /// ever taken over by a webmail write that carries guests. Most device PUTs stop here.</summary>
    private static bool NothingToSchedule(EventChange change, WriteOrigin origin) =>
        change.Before?.SchedulingOwner is null && (origin == WriteOrigin.Device || !HasAttendee(change.After));

    private static bool HasAttendee(string? ics) =>
        ics is not null && PartStatRewriter.Unfold(ics).Any(l => PartStatRewriter.IsProperty(l.Text, "ATTENDEE"));

    private async Task<SchedulingReport> RunAsync(
        User user, EventChange change, WriteOrigin origin,
        Func<CancellationToken, Task<MailAccountConnection?>>? session, string? language, CancellationToken cancellationToken)
    {
        var own = (await addresses.ForPrimaryAsync(user, CancellationToken.None)).ToHashSet(StringComparer.Ordinal);
        var before = new SchedulingBefore(change.Before?.Ics, change.Before?.SchedulingOwner, change.Before?.SchedulingHash);
        var decision = SchedulingDecider.Decide(new SchedulingInput(before, change.After, origin, own));

        var sent = 0;
        if (decision.Mails.Count > 0)
        {
            var source = change.After is not null && decision.Mails.Any(m => m.Kind == MailKind.Update)
                ? await AdvancedAsync(user, change, before.Ics, change.After)
                : change.After ?? before.Ics!;
            var connection = origin == WriteOrigin.Webmail && session is not null ? await session(CancellationToken.None) : null;
            sent = await SendAsync(user, change, decision.Mails, source, connection, language, own, cancellationToken);
        }

        if (decision.Owner != before.Owner || decision.Hash != before.Hash)
            await store.SetSchedulingAsync(user.WebmailUid, change.CalendarId, change.DavName, decision.Owner, decision.Hash, CancellationToken.None);
        return new SchedulingReport(decision.Owner, sent);
    }

    /// <summary>An update to invitees who hold a version, written by a client that changed a component without
    /// advancing its SEQUENCE: the server does, by one more write under the same name, conditional on the bytes
    /// just written so a write that landed meanwhile is never overwritten — an expected race. The file already
    /// stored stays the mail's source when it is refused.</summary>
    private async Task<string> AdvancedAsync(User user, EventChange change, string? before, string after)
    {
        if (before is null) return after;
        var lagging = SchedulingShape.ChangedWithoutBump(before, after);
        if (lagging.Count == 0) return after;

        var bumped = SequenceRewriter.Bump(after, lagging);
        var outcome = await writer.PutAsync(user.WebmailUid, change.CalendarId, change.DavName, bumped, CancellationToken.None,
            ifMatch: DavPropertyTables.EntityTag(IcsDocument.HashOf(after)), cause: RevisionCause.Scheduling);
        if (outcome.Status == DavWriteStatus.Replaced) return bumped;
        logger.Log(outcome.Status == DavWriteStatus.PreconditionFailed ? LogLevel.Information : LogLevel.Warning,
            "SEQUENCE could not be advanced on {DavName}: {Status}", change.DavName, outcome.Status);
        return after;
    }

    private async Task<int> SendAsync(
        User user, EventChange change, IReadOnlyList<ScheduledMail> mails, string source, MailAccountConnection? connection,
        string? language, IReadOnlySet<string> own, CancellationToken cancellationToken)
    {
        if (InvitationParser.Read(IcsMethod.With(source, "REQUEST")).Invitation is not { } parsed)
        {
            logger.LogWarning("The file of {DavName} does not read as an event; invitees not told", change.DavName);
            return 0;
        }

        var host = await HostOfAsync(user, parsed, own);
        var lang = language ?? await LanguageAsync(user);
        var cancelSequence = parsed.Sequence + (change.After is null ? 1 : 0);
        var now = clock.GetUtcNow().UtcDateTime;
        var sent = 0;
        foreach (var mail in mails)
        {
            var recipients = Deliverable(mail.Recipients, change.DavName);
            if (recipients.Count == 0) continue;
            var sequence = mail.Kind == MailKind.Cancellation ? cancelSequence : parsed.Sequence;
            var description = $"{mail.Kind} {parsed.Uid} seq {sequence} to {string.Join(", ", recipients)}";
            try
            {
                var message = InvitationMailer.Compose(new InvitationMailInput(mail.Kind, source, cancelSequence, recipients, host, lang, now));
                if (await DeliverAsync(user, connection, message, description, cancellationToken)) sent += recipients.Count;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Invitation mail not sent: {Description}", description);
            }
        }
        return sent;
    }

    /// <summary>From = the file's ORGANIZER, the address the replies come back to, when it is one of the primary
    /// account's addresses; the resolved identity otherwise, since the service account speaks for no other.</summary>
    private async Task<OrganizerWrite> HostOfAsync(User user, ParsedInvitation parsed, IReadOnlySet<string> own)
    {
        if (parsed.Organizer is { } o)
        {
            var named = o.Email.Trim().ToLowerInvariant();
            if (own.Contains(named)) return new OrganizerWrite(named, o.Name);
            logger.LogWarning("The ORGANIZER {Organizer} of {Uid} is not one of the primary account's addresses; the mail leaves under the resolved identity", named, parsed.Uid);
        }
        return await organizer.ResolveAsync(user, CancellationToken.None);
    }

    /// <summary>The recipients MimeKit accepts, the others logged and skipped: a device writes any ATTENDEE.</summary>
    private List<string> Deliverable(IReadOnlyList<string> recipients, string davName)
    {
        var deliverable = new List<string>(recipients.Count);
        foreach (var recipient in recipients)
        {
            if (InvitationMailer.Mailbox(recipient) is not null) deliverable.Add(recipient);
            else logger.LogWarning("Invitee {Recipient} of {DavName} is not a mail address; skipped", recipient, davName);
        }
        return deliverable;
    }

    /// <summary>The user's own session sends within the request, and is the only step bounded by it: once the
    /// request is aborted, this mail and the ones after it go by the queue — at worst a duplicate, never a loss.</summary>
    private async Task<bool> DeliverAsync(
        User user, MailAccountConnection? connection, MimeMessage message, string description, CancellationToken cancellationToken)
    {
        if (connection is not null && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var sent = await sender.SendBuiltAsync(user, connection, message, cancellationToken);
                if (sent.IsSuccess) logger.LogInformation("Invitation mail sent by the user's session: {Description}", description);
                else logger.LogWarning("Invitation mail refused by the user's session ({Error}): {Description}", sent.Error, description);
                return sent.IsSuccess;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("The request was aborted during the user's send; the service queue takes over: {Description}", description);
            }
        }
        return queue.TryEnqueue(new QueuedMail(message, description));
    }

    private async Task<string> LanguageAsync(User user)
    {
        var value = (await preferences.GetAsync(user.WebmailUid, CancellationToken.None))
            .FirstOrDefault(p => p.PreferenceKey == UserPreferences.UiLanguage)?.PreferenceValue;
        return value is not null && value.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "fr" : "en";
    }
}
