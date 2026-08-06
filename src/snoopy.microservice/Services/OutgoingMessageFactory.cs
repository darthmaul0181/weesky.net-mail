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
/// The one place an outgoing message is assembled, so Send and Drafts cannot drift apart. Order is
/// load-bearing: the From is validated before anything else runs, and staged ids resolve next — a
/// desync fails the whole request rather than producing a half-built message.
/// </summary>
internal sealed class OutgoingMessageFactory(
    IUsersRepository users,
    IAliasesRepository aliases,
    ISendingIdentityStore identities,
    IOutgoingMailSanitizer sanitizer,
    IStagedAttachmentStore staged,
    ILogger<OutgoingMessageFactory> logger) : IOutgoingMessageFactory
{
    public async Task<Result<MimeMessage>> CreateAsync(
        User user, MailAccountConnection connection, SendMessageRequest request, CancellationToken cancellationToken)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));
        ArgumentNullException.ThrowIfNull(connection);

        var userId = user.WebmailUid;
        // Deliberately narrower than "IsHomeServer && AccountId == primary": a shared mailbox on our
        // own server carries a GUID id, so it takes the connected path and never borrows the main
        // account's alias list. Safe direction — its From set is its own stored identities.
        var isPrimary = connection.AccountId == MailAccountConnection.Primary;
        var stored = await LoadIdentitiesAsync(userId, connection.StorageAccountId, cancellationToken);

        var from = isPrimary
            ? await ResolvePrimaryFromAsync(user, request.FromAddress, cancellationToken)
            : ResolveConnectedFrom(connection, stored, request.FromAddress);
        if (from.IsFailure) return Result.Failure<MimeMessage>(from.Error);
        var fromAddress = from.Value;

        var stagedScope = connection.StagedScope(user);
        var attachments = new List<StagedAttachment>();
        foreach (var id in request.AttachmentIds)
        {
            var attachment = staged.Open(stagedScope, id);
            if (attachment.IsFailure) return Result.Failure<MimeMessage>(IOutgoingMessageFactory.UnknownAttachment);
            attachments.Add(attachment.Value);
        }

        try
        {
            return Result.Success(
                await BuildMessageAsync(user, request, attachments, isPrimary, stored, fromAddress, cancellationToken));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // A staged file vanished between Open and read (TTL sweep / concurrent DELETE): 400, not 500.
            logger.LogWarning(ex, "Staged attachment vanished before the message could be built");
            return Result.Failure<MimeMessage>(IOutgoingMessageFactory.UnknownAttachment);
        }
    }

    /// <summary>The home mailbox: its own address, or one of its live aliases.</summary>
    private async Task<Result<string>> ResolvePrimaryFromAsync(
        User user, string? requestedFrom, CancellationToken cancellationToken)
    {
        var fromAddress = IdentityResolver.Canonical(user.Email);
        if (string.IsNullOrWhiteSpace(requestedFrom)) return fromAddress;

        var requested = IdentityResolver.Canonical(requestedFrom);
        // The primary is owned by definition, so the common case skips the alias round trip;
        // beyond it, the alias list — not the identity table — says what the user really owns.
        if (requested != fromAddress)
        {
            var owned = await aliases.GetAliasesAsync(user, cancellationToken);
            if (!IdentityResolver.Owns(owned.ToAddresses(), user.Email, requested))
                return Result.Failure<string>(IOutgoingMessageFactory.ForbiddenFrom);
        }
        return requested;
    }

    /// <summary>
    /// A connected account sends through its own server, so the home server's alias list says
    /// nothing about it: it owns its login address and whatever identities were stored for it.
    /// </summary>
    private static Result<string> ResolveConnectedFrom(
        MailAccountConnection connection, IReadOnlyList<SendingIdentity> stored, string? requestedFrom)
    {
        var own = IdentityResolver.Canonical(connection.Username);
        if (string.IsNullOrWhiteSpace(requestedFrom)) return own;

        var requested = IdentityResolver.Canonical(requestedFrom);
        return IdentityResolver.Owns(stored.Select(i => i.Address), own, requested)
            ? requested
            : Result.Failure<string>(IOutgoingMessageFactory.ForbiddenFrom);
    }

    private async Task<MimeMessage> BuildMessageAsync(
        User user, SendMessageRequest request, IReadOnlyList<StagedAttachment> attachments,
        bool isPrimary, IReadOnlyList<SendingIdentity> stored, string fromAddress, CancellationToken cancellationToken)
    {
        var linked = new List<StagedAttachment>();
        var regular = new List<StagedAttachment>();
        var builder = new BodyBuilder();

        if (request.TextBody is { } text)
        {
            // Text references no cid, so an inline part has nowhere to be shown: every staged file
            // travels as an ordinary attachment rather than being silently dropped. Nothing is
            // sanitized — there is no markup to judge, and the policy would mangle a literal '<'.
            builder.TextBody = text;
            regular.AddRange(attachments);
        }
        else
        {
            // The composer displays a staged inline image through its content URL; on the wire that
            // becomes a cid reference into the multipart/related. An image the user deleted from the
            // body has no URL left to rewrite: it is not packed, and still purged after the send.
            var html = request.HtmlBody;
            foreach (var attachment in attachments)
            {
                if (attachment.Info.ContentId == null) { regular.Add(attachment); continue; }
                // StagedContentUrl is the contract with QuotePreparer, the sole producer of these URLs.
                if (!StagedContentUrl.TryRewrite(html, attachment.Info.Id, $"cid:{attachment.Info.ContentId}", out html))
                    continue;
                linked.Add(attachment);
            }

            // Rewrite first, sanitize second: the outgoing policy keeps cid: and culls any leftover
            // relative URL, so no staged URL can survive into the wire format.
            var body = sanitizer.Prepare(html);

            // The sanitizer may still have dropped an image the raw body named; only what the final
            // body references gets packed, so no resource rides along unreferenced. Match on the decoded
            // body: the sanitizer's formatter escapes attribute values, so a Content-ID carrying '&' —
            // legal, and taken straight off the inbound part — reads back as "&amp;" and would be culled.
            if (linked.Count > 0)
            {
                var referenced = WebUtility.HtmlDecode(body.Html);
                linked.RemoveAll(a => !referenced.Contains($"cid:{a.Info.ContentId}", StringComparison.OrdinalIgnoreCase));
            }

            builder.HtmlBody = body.Html;
            builder.TextBody = body.Text;
        }

        var message = new MimeMessage();
        var label = await LabelForAsync(user, isPrimary, stored, fromAddress, cancellationToken);
        // LabelFor falls back to the address itself; on the wire that would be a redundant "a@x <a@x>".
        message.From.Add(new MailboxAddress(label == fromAddress ? string.Empty : label, fromAddress));
        AddAddresses(message.To, request.To);
        AddAddresses(message.Cc, request.Cc);
        AddAddresses(message.Bcc, request.Bcc);
        message.Subject = request.Subject;
        message.MessageId = MimeUtils.GenerateMessageId(DomainOf(fromAddress));
        ApplyThreadingHeaders(message, request);
        MailPriorityHeaders.Apply(message, request.Priority);

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

    /// <summary>
    /// The label the From carries. The home mailbox falls back to its FullName — read from the
    /// database, not the JWT claims; a connected account has only its stored rows, and no row (or
    /// a blank one) means the address travels alone rather than borrowing the main account's name.
    /// </summary>
    private async Task<string> LabelForAsync(
        User user, bool isPrimary, IReadOnlyList<SendingIdentity> stored, string fromAddress,
        CancellationToken cancellationToken)
    {
        if (!isPrimary)
            return stored.FirstOrDefault(i => IdentityResolver.Canonical(i.Address) == fromAddress)?.DisplayName
                   ?? string.Empty;

        var dbUser = await users.FindByEmailAsync(user.Email, cancellationToken);
        return IdentityResolver.LabelFor(stored, fromAddress, dbUser?.FullName, user.Email);
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
    /// An outage degrades to no rows rather than failing a send that would otherwise have gone
    /// out: the primary falls back to its own label, and a connected account is left with only its
    /// own address — refusing an unverifiable From is the safe direction, never granting one.
    /// </summary>
    private async Task<IReadOnlyList<SendingIdentity>> LoadIdentitiesAsync(
        Guid userId, string accountId, CancellationToken cancellationToken)
    {
        try
        {
            return await identities.GetAsync(userId, accountId, cancellationToken);
        }
        // Only the caller giving up propagates: a preferences layer surfacing its own timeout as
        // an OperationCanceledException is an outage like any other, and must degrade.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sending identities unavailable for {UserId}: using the account label", userId);
            return [];
        }
    }

    private static void AddAddresses(InternetAddressList list, IReadOnlyList<string> addresses)
    {
        foreach (var address in addresses) list.Add(MailboxAddress.Parse(address));
    }
}
