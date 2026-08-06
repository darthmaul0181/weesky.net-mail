using System.Text;
using CSharpFunctionalExtensions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;
using MimeKit.IO;
using MimeKit.Text;
using MimeKit.Utils;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The message half of the IMAP protocol adapter: listing, search, detail, attachments, flags,
/// move/copy/delete, append and drafts. Runs every operation through the owning session's
/// ExecuteAsync so the failure contract — cancellation, sentinels, the opaque message — stays
/// in one place.
/// </summary>
internal sealed class ImapMessageCommands(
    ImapSession session, ImapClient client, IMailHtmlSanitizer sanitizer, ILogger logger)
{
    public Task<Result> SetFlagsAsync(string folderPath, IReadOnlyList<uint> uids, MailFlag flag, bool value, CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await client.GetFolderAsync(folderPath, cancellationToken);
            // First ReadWrite open of the project: every read path stays ReadOnly.
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var messageFlags = flag == MailFlag.Seen ? MessageFlags.Seen : MessageFlags.Flagged;
            var ids = uids.Select(uid => new UniqueId(uid)).ToList();

            // A UID that no longer exists is a silent server-side no-op: the batch never fails partially.
            if (value) await folder.AddFlagsAsync(ids, messageFlags, silent: true, cancellationToken);
            else await folder.RemoveFlagsAsync(ids, messageFlags, silent: true, cancellationToken);

            return Result.Success();
        },
            "Unable to update the messages",
            ex => logger.LogError(ex, "Failed to set {Flag}={Value} on {Count} messages in {Folder}", flag, value, uids.Count, folderPath),
            ImapSession.FolderSentinel);

    public Task<Result> MoveOrCopyAsync(string folderPath, IReadOnlyList<uint> uids, string targetPath, bool copy, CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            var targetResult = await ImapSession.ResolveTargetOrFailAsync(client, targetPath, cancellationToken);
            if (targetResult.IsFailure) return Result.Failure(targetResult.Error);
            var target = targetResult.Value;

            var folder = await client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var ids = uids.Select(uid => new UniqueId(uid)).ToList();
            // MailKit uses MOVE when advertised and falls back to COPY + \Deleted + EXPUNGE itself.
            if (copy) await folder.CopyToAsync(ids, target, cancellationToken);
            else await folder.MoveToAsync(ids, target, cancellationToken);

            return Result.Success();
        },
            copy ? "Unable to copy the messages" : "Unable to move the messages",
            ex => logger.LogError(ex, "Failed to {Verb} {Count} messages from {Folder} to {Target}",
                copy ? "copy" : "move", uids.Count, folderPath, targetPath),
            ImapSession.FolderSentinel);

    public Task<Result> DeleteAsync(string folderPath, IReadOnlyList<uint> uids, CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            // A bare EXPUNGE purges every \Deleted message in the folder, including ones another
            // client marked and has not purged. UID EXPUNGE (UIDPLUS) limits it to ours — without
            // it, refusing beats widening the purge. Capabilities are read after authentication.
            if (!client.Capabilities.HasFlag(ImapCapabilities.UidPlus))
                return Result.Failure("The mail server cannot delete single messages (no UIDPLUS)");

            var folder = await client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var ids = uids.Select(uid => new UniqueId(uid)).ToList();
            await folder.AddFlagsAsync(ids, MessageFlags.Deleted, silent: true, cancellationToken);
            await folder.ExpungeAsync(ids, cancellationToken);

            return Result.Success();
        },
            "Unable to delete the messages",
            ex => logger.LogError(ex, "Failed to expunge {Count} messages from {Folder}", uids.Count, folderPath),
            ImapSession.FolderSentinel);

    public Task<Result> AppendAsync(string folderPath, MimeMessage message, bool seen, CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await client.GetFolderAsync(folderPath, cancellationToken);
            await folder.AppendAsync(message, seen ? MessageFlags.Seen : MessageFlags.None, cancellationToken);
            return Result.Success();
        },
            "Unable to file the message",
            ex => logger.LogError(ex, "Failed to append a message to {Folder}", folderPath),
            ImapSession.FolderSentinel);

    public Task<Result<uint>> SaveDraftAsync(
        string folderPath, MimeMessage message, uint? replaceUid, CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await client.GetFolderAsync(folderPath, cancellationToken);
            var appended = await folder.AppendAsync(message, MessageFlags.Draft | MessageFlags.Seen, cancellationToken);
            // No APPENDUID (UIDPLUS absent) would leave the composer unable to replace this version
            // on its next save, piling up one copy per save: refuse outright, like DeleteAsync does.
            if (appended == null)
                return Result.Failure<uint>("The mail server cannot track saved drafts (no UIDPLUS)");

            if (replaceUid is { } previous)
            {
                try
                {
                    await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
                    var ids = new List<UniqueId> { new(previous) };
                    await folder.AddFlagsAsync(ids, MessageFlags.Deleted, silent: true, cancellationToken);
                    await folder.ExpungeAsync(ids, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // The new version is already filed; an orphan predecessor is visible but harmless
                    // and goes with the folder's next manual cleanup.
                    logger.LogWarning(ex, "Could not remove replaced draft {Uid} in {Folder}", previous, folderPath);
                }
            }

            return Result.Success(appended.Value.Id);
        },
            "Unable to save the draft",
            ex => logger.LogError(ex, "Failed to save a draft in {Folder}", folderPath),
            ImapSession.FolderSentinel);

    public Task<Result<MailSearchPage>> SearchAsync(
        string folderPath, bool allFolders, MailSearchCriteria criteria, int page, int pageSize, CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            var query = MailSearchQueryBuilder.Build(criteria, DateTime.UtcNow.Date);
            var result = new MailSearchPage { Page = page, PageSize = pageSize };

            // Every match as (folder, uid), already newest-first once this list is final.
            List<(IMailFolder Folder, UniqueId Uid)> matches;

            if (!allFolders && client.Capabilities.HasFlag(ImapCapabilities.Sort))
            {
                // Single folder with SORT: the server hands the order, no dates needed.
                var folder = await client.GetFolderAsync(folderPath, cancellationToken);
                await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

                var sorted = await folder.SortAsync(query, [OrderBy.ReverseDate], cancellationToken);
                var uids = criteria.HasAttachment
                    ? await WithAttachmentsAsync(
                        folder, MailPaging.BoundCandidates(sorted, page, pageSize, MailPaging.AttachmentScanBudget), cancellationToken)
                    : sorted;
                matches = uids.Select(uid => (folder, uid)).ToList();
                result.Total = criteria.HasAttachment ? matches.Count : sorted.Count;
            }
            else
            {
                // All folders — or a server without SORT: per folder, SORT (REVERSE DATE) when the
                // session advertises it (rule 2), then one FETCH for the merge key (Envelope.Date,
                // arrival as fallback) bounded to the (page + 1) * pageSize candidates that can
                // still reach the requested page — widened to the attachment scan budget when that
                // post-filter rides the same FETCH. Without SORT no order exists until the dates
                // arrive — UID order is arrival order, exactly what rule 2 says is not date order —
                // so every match's key is fetched (items, never bodies) and the attachment filter
                // becomes a budgeted second phase over the merged date order instead.
                var sortCapable = client.Capabilities.HasFlag(ImapCapabilities.Sort);
                var fetchItems = MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope
                                 | MessageSummaryItems.InternalDate;
                if (sortCapable && criteria.HasAttachment) fetchItems |= MessageSummaryItems.BodyStructure;

                // Pass 1 — SEARCH/SORT only: bare UID lists, cheap whatever the folders hold,
                // and the whole picture the budget split below needs before anything is fetched.
                var perFolder = new List<(IMailFolder Folder, IList<UniqueId> Found)>();
                foreach (var folder in await SearchableFoldersAsync(folderPath, allFolders, cancellationToken))
                {
                    await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
                    IList<UniqueId> found = sortCapable
                        ? await folder.SortAsync(query, [OrderBy.ReverseDate], cancellationToken)
                        : await folder.SearchAsync(query, cancellationToken);
                    if (found.Count > 0) perFolder.Add((folder, found));
                }

                var exactTotal = perFolder.Sum(entry => entry.Found.Count);

                // The attachment budget splits across folders by need, never by the order the
                // folder list arrived in; each folder always keeps its page window regardless.
                var shares = sortCapable && criteria.HasAttachment
                    ? MailPaging.FairShares(perFolder.Select(entry => entry.Found.Count).ToList(), MailPaging.AttachmentScanBudget)
                    : null;

                // Pass 2 — the bounded key fetch. Each folder is re-opened: IMAP selects one
                // mailbox at a time, and pass 1 left only the last one selected.
                var hits = new List<MailPaging.SearchHit>();
                for (var i = 0; i < perFolder.Count; i++)
                {
                    var (folder, found) = perFolder[i];
                    await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

                    var candidates = sortCapable
                        ? MailPaging.BoundCandidates(found, page, pageSize, shares?[i] ?? 0L)
                        : found;

                    var items = await folder.FetchAsync(candidates, fetchItems, cancellationToken);
                    hits.AddRange(items.Select(item => new MailPaging.SearchHit(
                        item.UniqueId, folder,
                        MailPaging.SortKeyOf(item.Envelope?.Date, item.InternalDate),
                        item.Attachments?.Any() ?? false)));
                }

                if (criteria.HasAttachment && !sortCapable)
                {
                    // Keys for every match are in hand, BODYSTRUCTURE for none: examine only the
                    // newest budget's worth, chosen on the merged date order.
                    var examined = MailPaging.OrderHits(hits, attachmentsOnly: false)
                        .Take(MailPaging.ExamineLimit(page, pageSize)).ToList();
                    hits = await WhereAttachmentsAsync(examined, cancellationToken);
                }

                // Filter (attachments) and sort BEFORE pagination. Under the post-filter only the
                // budgeted candidates were ever examined, so Total is "at least" what is counted
                // here; without it, SEARCH already said exactly how many match.
                matches = MailPaging.OrderHits(hits, criteria.HasAttachment)
                    .Select(hit => (hit.Folder, hit.Uid)).ToList();
                result.Total = criteria.HasAttachment ? matches.Count : exactTotal;
            }

            var wanted = MailPaging.PageOf(matches, page, pageSize);
            if (wanted.Count == 0) return Result.Success(result);

            // One summary fetch per folder present in the page. Each folder is re-opened:
            // IMAP selects one mailbox at a time, so the loop above left only the last one open.
            var byKey = new Dictionary<(string, uint), MailSearchResult>();
            foreach (var group in wanted.GroupBy(m => m.Folder))
            {
                await group.Key.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
                var items = await group.Key.FetchAsync(
                    group.Select(m => m.Uid).ToList(), SummaryItems, SummaryHeaders, cancellationToken);
                foreach (var item in items)
                {
                    byKey[(group.Key.FullName, item.UniqueId.Id)] = MailMessageMapper.FillSummary(new MailSearchResult
                    {
                        FolderPath = group.Key.FullName,
                        UidValidity = group.Key.UidValidity,
                    }, item);
                }
            }

            // Back into merged order; a uid expunged between search and fetch just drops out.
            foreach (var match in wanted)
            {
                if (byKey.TryGetValue((match.Folder.FullName, match.Uid.Id), out var row))
                    result.Results.Add(row);
            }

            return Result.Success(result);
        },
            "Unable to search the messages",
            ex => logger.LogError(ex, "Failed to search messages from {Folder} (allFolders: {AllFolders})", folderPath, allFolders),
            ImapSession.FolderSentinel);

    /// <summary>The folders one search sweeps: the named one, or every selectable folder.</summary>
    private async Task<IReadOnlyList<IMailFolder>> SearchableFoldersAsync(
        string folderPath, bool allFolders, CancellationToken cancellationToken)
    {
        if (!allFolders) return [await client.GetFolderAsync(folderPath, cancellationToken)];

        var folders = await client.GetFoldersAsync(client.PersonalNamespaces[0], cancellationToken: cancellationToken);
        return folders
            .Where(f => (f.Attributes & (FolderAttributes.NonExistent | FolderAttributes.NoSelect)) == 0)
            .ToList();
    }

    /// <summary>
    /// Keeps only the matches whose BODYSTRUCTURE shows an attachment — the same predicate
    /// that fills HasAttachments. Runs before paging: filtering after would falsify Total.
    /// </summary>
    private static async Task<IList<UniqueId>> WithAttachmentsAsync(
        IMailFolder folder, IList<UniqueId> uids, CancellationToken cancellationToken)
    {
        if (uids.Count == 0) return uids;

        var items = await folder.FetchAsync(
            uids, MessageSummaryItems.UniqueId | MessageSummaryItems.BodyStructure, cancellationToken);
        var keep = items.Where(i => i.Attachments?.Any() ?? false).Select(i => i.UniqueId).ToHashSet();
        return uids.Where(keep.Contains).ToList();
    }

    /// <summary>
    /// Keeps the hits whose BODYSTRUCTURE shows an attachment — the non-SORT second phase,
    /// fetched per folder for exactly the hits given, after the date merge chose them.
    /// </summary>
    private static async Task<List<MailPaging.SearchHit>> WhereAttachmentsAsync(
        List<MailPaging.SearchHit> hits, CancellationToken cancellationToken)
    {
        var kept = new List<MailPaging.SearchHit>();
        foreach (var group in hits.GroupBy(hit => hit.Folder))
        {
            await group.Key.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
            var keep = (await WithAttachmentsAsync(
                group.Key, group.Select(hit => hit.Uid).ToList(), cancellationToken)).ToHashSet();
            kept.AddRange(group.Where(hit => keep.Contains(hit.Uid))
                .Select(hit => hit with { HasAttachment = true }));
        }

        return kept;
    }

    public Task<Result<MailFolderPage>> ListMessagesAsync(string folderPath, int page, int pageSize, CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var result = new MailFolderPage
            {
                FolderPath = folder.FullName,
                UidValidity = folder.UidValidity,
                Total = folder.Count,
                Page = page,
                PageSize = pageSize
            };

            // SORT asks the server for date order. Without it the page is a window on the
            // sequence numbers, which is arrival-into-the-folder order — the same thing in an
            // inbox, but not in a folder messages are *moved* to: a trash lists by when each
            // message was thrown away, not by its date.
            if (client.Capabilities.HasFlag(ImapCapabilities.Sort))
            {
                var sorted = await folder.SortAsync(
                    SearchQuery.All, [OrderBy.ReverseDate], cancellationToken);

                var wanted = MailPaging.PageOf(sorted, page, pageSize).ToList();
                if (wanted.Count == 0) return Result.Success(result);

                var sortedItems = await folder.FetchAsync(wanted, SummaryItems, SummaryHeaders, cancellationToken);
                foreach (var item in MailPaging.InOrderOf(sortedItems, wanted, item => item.UniqueId))
                {
                    result.Messages.Add(ToSummary(item));
                }

                return Result.Success(result);
            }

            var (start, end) = MailPaging.ComputePageWindow(folder.Count, page, pageSize);
            if (start < 0) return Result.Success(result);

            var items = await folder.FetchAsync(start, end, SummaryItems, SummaryHeaders, cancellationToken);

            // The fetch runs oldest-first; the list is newest-first.
            foreach (var item in items.Reverse())
            {
                result.Messages.Add(ToSummary(item));
            }

            return Result.Success(result);
        },
            "Unable to read the messages",
            ex => logger.LogError(ex, "Failed to list messages in {Folder}", folderPath),
            ImapSession.FolderSentinel);

    private const MessageSummaryItems SummaryItems =
        MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
        MessageSummaryItems.Size | MessageSummaryItems.BodyStructure | MessageSummaryItems.InternalDate |
        MessageSummaryItems.PreviewText;

    /// <summary>
    /// Priority is not in the envelope, so the summary FETCH has to name its headers. On the wire
    /// this is one BODY.PEEK[HEADER.FIELDS (...)] alongside the items above — one more item in the
    /// same round trip, not a second request, and the price of showing priority in the list at all.
    /// </summary>
    private static readonly string[] SummaryHeaders = MailPriorityReader.Fields;

    private static MailMessageSummary ToSummary(IMessageSummary item) =>
        MailMessageMapper.FillSummary(new MailMessageSummary(), item);

    public Task<Result<MailMessageDetail>> GetMessageAsync(string folderPath, uint uid, CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var uniqueId = new UniqueId(folder.UidValidity, uid);

            var summaries = await folder.FetchAsync(
                new[] { uniqueId },
                MessageSummaryItems.UniqueId | MessageSummaryItems.BodyStructure,
                cancellationToken);

            var summary = summaries.FirstOrDefault();
            if (summary == null) return Result.Failure<MailMessageDetail>(ImapSession.MessageNotFound);

            // Never BODY.PEEK[] here: the whole-message fetch pulled every attachment over the
            // wire to render a few KB of body. BODYSTRUCTURE names the text parts, so only those
            // and the header block are fetched; attachments stay on the server until clicked.
            var headers = await folder.GetHeadersAsync(uniqueId, cancellationToken);
            var message = ToHeaderOnlyMessage(headers);

            // IsAttachment: MimeKit refuses an attachment-disposed part as the body; MailKit's
            // non-multipart TextBody/HtmlBody branch does not check, so the guard sits here.
            var htmlBody = summary.HtmlBody is { IsAttachment: false } htmlPart
                ? await ReadTextPartAsync(folder, uniqueId, htmlPart, cancellationToken)
                : null;
            var textBody = summary.TextBody is { IsAttachment: false } textPart
                ? await ReadTextPartAsync(folder, uniqueId, textPart, cancellationToken)
                : null;

            var sanitized = sanitizer.Sanitize(htmlBody ?? string.Empty);
            var sender = message.From?.Mailboxes?.FirstOrDefault();
            var headerDetails = MailHeaderDetailsReader.Parse(message.Headers);

            var detail = new MailMessageDetail
            {
                Uid = uid,
                FolderPath = folder.FullName,
                UidValidity = folder.UidValidity,
                Subject = message.Subject ?? string.Empty,
                FromName = sender?.Name is { Length: > 0 } name ? name : sender?.Address ?? string.Empty,
                FromAddress = sender?.Address ?? string.Empty,
                To = MailMessageMapper.ToAddressInfos(message.To),
                Cc = MailMessageMapper.ToAddressInfos(message.Cc),
                Date = message.Date,
                HtmlBody = sanitized.Html,
                TextBody = textBody ?? string.Empty,
                BlockedImageCount = sanitized.BlockedImageCount,
                Truncated = sanitized.Truncated,
                Authentication = MailAuthenticationReader.Parse(message.Headers),
                SpamScore = MailSpamScoreReader.Parse(message.Headers),
                MailingList = headerDetails.MailingList,
                SentBy = headerDetails.SentBy,
                SignedBy = headerDetails.SignedBy,
                UnsubscribeUrl = headerDetails.UnsubscribeUrl,
                TlsReceived = headerDetails.TlsReceived,
                Priority = MailPriorityReader.Parse(message.Headers)
            };

            MailMessageMapper.ApplyThreading(detail, message);

            foreach (var part in summary.BodyParts.OfType<BodyPartBasic>())
            {
                if (!MailMessageMapper.IsListedPart(part)) continue;

                detail.Attachments.Add(new MailAttachmentInfo
                {
                    Part = part.PartSpecifier,
                    FileName = string.IsNullOrEmpty(part.FileName) ? "attachment" : part.FileName,
                    ContentType = part.ContentType?.MimeType ?? "application/octet-stream",
                    Size = part.Octets,
                    IsInline = !part.IsAttachment,
                    ContentId = MailMessageMapper.TrimAngleBrackets(part.ContentId)
                });
            }

            return Result.Success(detail);
        },
            "Unable to read the message",
            ex => logger.LogError(ex, "Failed to read message {Uid} in {Folder}", uid, folderPath),
            ImapSession.FolderOrMessageSentinel);

    /// <summary>
    /// Rehydrates a fetched header block into a MimeMessage, so every header-derived field —
    /// envelope, threading (In-Reply-To/References), the four readers — keeps MimeKit's exact
    /// decoding, byte-identical to when the whole message was downloaded and parsed.
    /// </summary>
    internal static MimeMessage ToHeaderOnlyMessage(HeaderList headers)
    {
        using var buffer = new MemoryStream();
        headers.WriteTo(buffer);
        buffer.Position = 0;
        return MimeMessage.Load(buffer);
    }

    /// <summary>
    /// IMAP spells "the body of a non-multipart message" TEXT, not an empty section — an empty
    /// section is the whole RFC822 message, headers included.
    /// </summary>
    internal static string SectionOf(BodyPartBasic part) =>
        part.PartSpecifier.Length == 0 ? "TEXT" : part.PartSpecifier;

    /// <summary>
    /// Fetches one text part by its specifier and decodes it the way MimeKit decodes a parsed
    /// message's body — same transfer decoding, same charset handling and fallbacks — so a
    /// quoted-printable accented body reads exactly as it did off the whole-message fetch.
    /// </summary>
    private static async Task<string> ReadTextPartAsync(
        IMailFolder folder, UniqueId uniqueId, BodyPartText part, CancellationToken cancellationToken)
    {
        using var encoded = await folder.GetStreamAsync(uniqueId, SectionOf(part), cancellationToken);
        // A NIL body_fld_enc reads as the default encoding. TryParse tolerates null on this
        // MimeKit version (verified), but that is its internals, not its contract.
        MimeUtils.TryParse(part.ContentTransferEncoding ?? string.Empty, out ContentEncoding encoding);

        var textPart = new TextPart(part.ContentType.MediaSubtype)
        {
            Content = new MimeContent(encoded, encoding)
        };
        foreach (var parameter in part.ContentType.Parameters)
            textPart.ContentType.Parameters[parameter.Name] = parameter.Value;

        return UnflowText(textPart);
    }

    /// <summary>
    /// MimeMessage.TextBody unflows RFC 3676 format=flowed — Thunderbird's and Apple Mail's
    /// default — before returning, via MimeKit's internal MultipartAlternative.GetText; that
    /// helper is not public, so its conversion is mirrored here. TextPart.Text alone would hand
    /// the reader ~72-column hard wraps, space-stuffed quotes and delsp=yes stray spaces.
    /// </summary>
    internal static string UnflowText(TextPart textPart)
    {
        if (!textPart.IsFlowed) return textPart.Text;

        var converter = new FlowedToText();
        if (textPart.ContentType.Parameters.TryGetValue("delsp", out string? delsp))
            converter.DeleteSpace = delsp.Equals("yes", StringComparison.OrdinalIgnoreCase);

        return converter.Convert(textPart.Text);
    }

    /// <summary>The message as MimeKit parsed it — PrepareQuote needs the raw body and its parts.</summary>
    public Task<Result<MimeMessage>> GetMimeMessageAsync(string folderPath, uint uid, CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
            var message = await folder.GetMessageAsync(new UniqueId(folder.UidValidity, uid), cancellationToken);
            return Result.Success(message);
        },
            "Unable to read the message",
            ex => logger.LogError(ex, "Failed to read raw message {Uid} in {Folder}", uid, folderPath),
            ImapSession.MessageSentinel);

    /// <summary>
    /// Two IMAP round trips: an envelope-plus-size fetch that also pulls the
    /// Authentication-Results header, then a BODY[]&lt;0.N&gt; partial fetch for the bytes.
    /// </summary>
    public Task<Result<MailMessageSource>> GetMessageSourceAsync(
        string folderPath, uint uid, int maxBytes, CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var uniqueId = new UniqueId(folder.UidValidity, uid);

            var summaries = await folder.FetchAsync(
                new[] { uniqueId },
                MessageSummaryItems.Envelope | MessageSummaryItems.Size,
                new[] { HeaderId.AuthenticationResults },
                cancellationToken);

            var summary = summaries.FirstOrDefault();
            if (summary?.Envelope == null) return Result.Failure<MailMessageSource>(ImapSession.MessageNotFound);

            using var stream = await folder.GetStreamAsync(uniqueId, 0, maxBytes, cancellationToken);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();

            // UTF-8 with .NET's replacing decoder: headers are ASCII either way, and a modern
            // 8-bit body is UTF-8 far more often than it is anything else. A sequence cut in
            // half by the cap costs one replacement character at the very tail.
            var source = Encoding.UTF8.GetString(bytes);

            var envelope = summary.Envelope;
            var from = envelope.From.Mailboxes.FirstOrDefault();

            // bytes.LongLength can never exceed maxBytes (the stream itself is capped there), so
            // when the server omits RFC822.SIZE there is no ground truth to compare against and
            // IsTruncated's ">" would always read false. Erring toward "there may be more": a
            // full-cap read is treated as truncated rather than trusted as the whole message.
            long total;
            bool truncated;
            if (summary.Size.HasValue)
            {
                total = (long)summary.Size.Value;
                truncated = MailMessageSource.IsTruncated(total, maxBytes);
            }
            else
            {
                total = bytes.LongLength;
                truncated = bytes.LongLength >= maxBytes;
            }

            return Result.Success(new MailMessageSource(
                Subject: envelope.Subject ?? string.Empty,
                MessageId: MailMessageMapper.TrimAngleBrackets(envelope.MessageId),
                Date: envelope.Date ?? DateTimeOffset.MinValue,
                FromName: from?.Name ?? string.Empty,
                FromAddress: from?.Address ?? string.Empty,
                To: MailMessageMapper.ToAddressInfos(envelope.To),
                Authentication: MailAuthenticationReader.Parse(summary.Headers ?? []),
                Source: source,
                TotalBytes: total,
                Truncated: truncated));
        },
            "Unable to read the message source",
            ex => logger.LogError(ex, "Failed to read the source of {Uid} in {Folder}", uid, folderPath),
            ImapSession.MessageSentinel);

    public Task<Result<MailAttachmentContent>> GetAttachmentAsync(string folderPath, uint uid, string partSpecifier, CancellationToken cancellationToken) =>
        session.ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var uniqueId = new UniqueId(folder.UidValidity, uid);

            var summaries = await folder.FetchAsync(new[] { uniqueId }, MessageSummaryItems.BodyStructure, cancellationToken);
            var summary = summaries.FirstOrDefault();
            if (summary == null) return Result.Failure<MailAttachmentContent>(ImapSession.MessageNotFound);

            var part = summary.BodyParts.OfType<BodyPartBasic>()
                .FirstOrDefault(p => string.Equals(p.PartSpecifier, partSpecifier, StringComparison.Ordinal));
            if (part == null) return Result.Failure<MailAttachmentContent>(ImapSession.AttachmentNotFound);

            // Verified on MailKit 4.17: every Get* materialises the literal into a backing stream
            // before returning — there is no socket-to-response path. GetStreamAsync still beats
            // GetBodyPartAsync: no [n.MIME] sub-fetch, no MimeParser pass, and BODYSTRUCTURE in
            // hand already says how to decode. Decoding lands in MemoryBlockStream — seekable for
            // Content-Length, pooled 2 KB blocks, so no large-object-heap buffer and no resizing.
            using var encoded = await folder.GetStreamAsync(uniqueId, SectionOf(part), cancellationToken);
            // A NIL body_fld_enc reads as the default encoding. TryParse tolerates null on this
            // MimeKit version (verified), but that is its internals, not its contract.
            MimeUtils.TryParse(part.ContentTransferEncoding ?? string.Empty, out ContentEncoding encoding);

            var decoded = new MemoryBlockStream();
            await new MimeContent(encoded, encoding).DecodeToAsync(decoded, cancellationToken);
            decoded.Position = 0;

            return Result.Success(new MailAttachmentContent
            {
                Content = decoded,
                FileName = string.IsNullOrEmpty(part.FileName) ? "attachment" : part.FileName,
                ContentType = part.ContentType?.MimeType ?? "application/octet-stream"
            });
        },
            "Unable to read the attachment",
            ex => logger.LogError(ex, "Failed to read attachment {Part} of message {Uid}", partSpecifier, uid),
            ImapSession.FolderOrMessageSentinel);
}
