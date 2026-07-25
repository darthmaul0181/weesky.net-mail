using System.Net;
using CSharpFunctionalExtensions;
using MimeKit;
using MimeKit.Utils;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The send pipeline. Order is load-bearing: the From is validated before anything reaches the
/// wire, staged ids resolve next (a desync fails the whole request, never a partial send),
/// SMTP failure keeps the staged files for a retry, and once SMTP accepted, nothing after it
/// may fail the operation — the mail is gone.
/// </summary>
internal sealed class MailSender : IMailSender
{
    private readonly IUsersRepository _users;
    private readonly IAliasesRepository _aliases;
    private readonly ISendingIdentityStore _identities;
    private readonly IOutgoingMailSanitizer _sanitizer;
    private readonly IStagedAttachmentStore _staged;
    private readonly ISmtpConnectionFactory _smtpFactory;
    private readonly IMailFolderRepository _folders;
    private readonly IFolderRoleStore _roles;
    private readonly IMailMessageRepository _messages;
    private readonly ILogger<MailSender> _logger;

    public MailSender(
        IUsersRepository users,
        IAliasesRepository aliases,
        ISendingIdentityStore identities,
        IOutgoingMailSanitizer sanitizer,
        IStagedAttachmentStore staged,
        ISmtpConnectionFactory smtpFactory,
        IMailFolderRepository folders,
        IFolderRoleStore roles,
        IMailMessageRepository messages,
        ILogger<MailSender> logger)
    {
        _users = users;
        _aliases = aliases;
        _identities = identities;
        _sanitizer = sanitizer;
        _staged = staged;
        _smtpFactory = smtpFactory;
        _folders = folders;
        _roles = roles;
        _messages = messages;
        _logger = logger;
    }

    public async Task<Result<SendMessageResult>> SendAsync(
        User user, string password, SendMessageRequest request, CancellationToken cancellationToken)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));

        var userId = user.WebmailUid;

        var fromAddress = IdentityResolver.Canonical(user.Email);
        if (!string.IsNullOrWhiteSpace(request.FromAddress))
        {
            var requested = IdentityResolver.Canonical(request.FromAddress);
            // The primary is owned by definition, so the common case skips the alias round trip;
            // beyond it, the alias list — not the identity table — says what the user really owns.
            if (requested != fromAddress)
            {
                var owned = await _aliases.GetAliasesAsync(user);
                if (!IdentityResolver.Owns(owned.ToAddresses(), user.Email, requested))
                    return Result.Failure<SendMessageResult>(IMailSender.ForbiddenFrom);
            }
            fromAddress = requested;
        }

        var attachments = new List<StagedAttachment>();
        foreach (var id in request.AttachmentIds)
        {
            var attachment = _staged.Open(userId.ToString(), id);
            if (attachment.IsFailure) return Result.Failure<SendMessageResult>(IMailSender.UnknownAttachment);
            attachments.Add(attachment.Value);
        }

        MimeMessage message;
        try
        {
            message = await BuildMessageAsync(user, request, attachments, userId, fromAddress, cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // A staged file vanished between Open and read (TTL sweep / concurrent DELETE): 400, not 500.
            _logger.LogWarning(ex, "Staged attachment vanished before send");
            return Result.Failure<SendMessageResult>(IMailSender.UnknownAttachment);
        }

        var smtp = await _smtpFactory.OpenAsync(user.Email, password, cancellationToken);
        if (smtp.IsFailure) return Result.Failure<SendMessageResult>(smtp.Error);
        await using (var session = smtp.Value)
        {
            var sent = await session.SendAsync(message, cancellationToken);
            if (sent.IsFailure) return Result.Failure<SendMessageResult>(sent.Error);
        }

        var appended = await AppendToSentAsync(user, password, userId, message, cancellationToken);

        foreach (var id in request.AttachmentIds) _staged.Delete(userId.ToString(), id);

        return Result.Success(new SendMessageResult(appended));
    }

    private async Task<MimeMessage> BuildMessageAsync(
        User user, SendMessageRequest request, IReadOnlyList<StagedAttachment> attachments,
        Guid userId, string fromAddress, CancellationToken cancellationToken)
    {
        // FullName lives in the database, not in the JWT claims.
        var dbUser = await _users.FindByEmailAsync(user.Email);

        // The composer displays a staged inline image through its content URL; on the wire that
        // becomes a cid reference into the multipart/related. An image the user deleted from the
        // body has no URL left to rewrite: it is not packed, and still purged after the send.
        var html = request.HtmlBody;
        var linked = new List<StagedAttachment>();
        var regular = new List<StagedAttachment>();
        foreach (var attachment in attachments)
        {
            if (attachment.Info.ContentId == null) { regular.Add(attachment); continue; }
            var url = $"/api/Mail/Attachments/{attachment.Info.Id}/content";
            if (!html.Contains(url, StringComparison.OrdinalIgnoreCase)) continue;
            // This relative form is a contract with QuotePreparer, the sole producer of these URLs.
            html = html.Replace(url, $"cid:{attachment.Info.ContentId}", StringComparison.OrdinalIgnoreCase);
            linked.Add(attachment);
        }

        // Rewrite first, sanitize second: the outgoing policy keeps cid: and culls any leftover
        // relative URL, so no staged URL can survive into the wire format.
        var body = _sanitizer.Prepare(html);

        // The sanitizer may still have dropped an image the raw body named; only what the final
        // body references gets packed, so no resource rides along unreferenced. Match on the decoded
        // body: the sanitizer's formatter escapes attribute values, so a Content-ID carrying '&' —
        // legal, and taken straight off the inbound part — reads back as "&amp;" and would be culled.
        if (linked.Count > 0)
        {
            var referenced = WebUtility.HtmlDecode(body.Html);
            linked.RemoveAll(a => !referenced.Contains($"cid:{a.Info.ContentId}", StringComparison.OrdinalIgnoreCase));
        }

        var message = new MimeMessage();
        var stored = await LoadIdentitiesAsync(userId, cancellationToken);
        var label = IdentityResolver.LabelFor(stored, fromAddress, dbUser?.FullName, user.Email);
        // LabelFor falls back to the address itself; on the wire that would be a redundant "a@x <a@x>".
        message.From.Add(new MailboxAddress(label == fromAddress ? string.Empty : label, fromAddress));
        AddAddresses(message.To, request.To);
        AddAddresses(message.Cc, request.Cc);
        AddAddresses(message.Bcc, request.Bcc);
        message.Subject = request.Subject;
        message.MessageId = MimeUtils.GenerateMessageId(DomainOf(fromAddress));
        ApplyThreadingHeaders(message, request);

        var builder = new BodyBuilder { HtmlBody = body.Html, TextBody = body.Text };
        foreach (var attachment in linked)
        {
            await using var content = File.OpenRead(attachment.FilePath);
            var resource = ContentType.TryParse(attachment.Info.ContentType, out var linkedType)
                ? await builder.LinkedResources.AddAsync(attachment.Info.FileName, content, linkedType, cancellationToken)
                : await builder.LinkedResources.AddAsync(attachment.Info.FileName, content, cancellationToken);
            resource.ContentId = attachment.Info.ContentId;
        }
        foreach (var attachment in regular)
        {
            await using var content = File.OpenRead(attachment.FilePath);
            if (ContentType.TryParse(attachment.Info.ContentType, out var contentType))
                await builder.Attachments.AddAsync(attachment.Info.FileName, content, contentType, cancellationToken);
            else
                await builder.Attachments.AddAsync(attachment.Info.FileName, content, cancellationToken);
        }

        message.Body = builder.ToMessageBody();
        return message;
    }

    private static string DomainOf(string address)
    {
        var at = address.LastIndexOf('@');
        return at >= 0 && at < address.Length - 1 ? address[(at + 1)..] : "localhost";
    }

    /// <summary>
    /// Threading is best-effort and per-id. The client controls these ids, and neither
    /// <c>MessageIdList.Add</c> nor the <c>InReplyTo</c> setter rejects a CRLF-bearing one — appended
    /// verbatim, it would inject a header line into a message we are about to DKIM-sign. Parsing each
    /// id keeps only the msg-id itself, so a malformed one is dropped and none can ever fail a send.
    /// </summary>
    private static void ApplyThreadingHeaders(MimeMessage message, SendMessageRequest request)
    {
        if (request.InReplyTo is { } parent && MimeUtils.ParseMessageId(parent) is { } parsed)
            message.InReplyTo = parsed;
        foreach (var reference in request.References)
            if (reference is not null && MimeUtils.ParseMessageId(reference) is { } id)
                message.References.Add(id);
    }

    /// <summary>
    /// The preferences database only carries display labels, so an outage degrades to the account's
    /// own label rather than failing a send that would otherwise have gone out.
    /// </summary>
    private async Task<IReadOnlyList<SendingIdentity>> LoadIdentitiesAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            return await _identities.GetAsync(userId, cancellationToken);
        }
        // Only the caller giving up propagates: a preferences layer surfacing its own timeout as
        // an OperationCanceledException is an outage like any other, and must degrade.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sending identities unavailable for {UserId}: using the account label", userId);
            return [];
        }
    }

    private static void AddAddresses(InternetAddressList list, IReadOnlyList<string> addresses)
    {
        foreach (var address in addresses) list.Add(MailboxAddress.Parse(address));
    }

    /// <summary>Best-effort by design: the mail is already gone, so every failure degrades to false.</summary>
    private async Task<bool> AppendToSentAsync(
        User user, string password, Guid userId, MimeMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var tree = await _folders.GetTreeAsync(user, password, cancellationToken);
            if (tree.IsFailure) { _logger.LogWarning("No Sent copy: folder tree unavailable"); return false; }

            var overrides = await _roles.GetAsync(userId, cancellationToken);
            var sent = FolderRoleResolver.Resolve(tree.Value, overrides).Roles
                .FirstOrDefault(r => r.Role == "sent" && r.FolderPath != null);
            if (sent == null) { _logger.LogWarning("No Sent copy: no folder holds the sent role"); return false; }

            var appended = await _messages.AppendAsync(user, password, sent.FolderPath!, message, seen: true, cancellationToken);
            if (appended.IsFailure) _logger.LogWarning("No Sent copy: {Error}", appended.Error);
            return appended.IsSuccess;
        }
        catch (Exception ex)
        {
            // The mail is already sent; a raw throw here (e.g. preferences DB down) must never
            // fail the request, or the user resends and duplicates it.
            _logger.LogError(ex, "No Sent copy: filing the sent message threw for {UserId}", userId);
            return false;
        }
    }
}
