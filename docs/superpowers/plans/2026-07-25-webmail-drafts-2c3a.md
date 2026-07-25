# Webmail Drafts (2c3a) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Save, resume and send drafts as standard MIME messages in the IMAP drafts-role folder, with the Drafts list showing recipients and opening straight into the composer.

**Architecture:** The outgoing-message construction is extracted from `MailSender` into a shared `OutgoingMessageFactory` used by both Send and a new `DraftSaver` (APPEND `\Draft \Seen` + replace-previous via UID EXPUNGE). Resuming reuses `QuotePreparer`'s `EditAsNew` machinery (outbound-sanitised body, cid→staged URLs, server-side re-staging) plus envelope extraction, feeding the existing `ComposeSeed`. Spec: `docs/superpowers/specs/2026-07-25-webmail-drafts-2c3a-design.md`.

**Tech Stack:** ASP.NET Core (.NET 10), MailKit/MimeKit, xUnit+Moq; React 18/TS, TanStack Query v5, Vitest.

## Global Constraints

- All code, comments and UI copy in **English**.
- Backend style: file-scoped namespaces, records for DTOs, `internal sealed` by default, primary constructors for DI, cancellation tokens on async methods, `Result<T>` error handling, structured logging only.
- Backend errors: 401 `credentials_unavailable`, 502 for anything the mail server refuses, 404 via the shared `ImapSession.MessageNotFound` constant, 400 for validation.
- Frontend: query keys are account-scoped `['mail', accountId, …]` (use `mailKeys`); folder paths never in a route segment; staged-URL absolutize/relativize invariants from 2c2b stay pinned.
- `dotnet test` from `src/snoopy.microservice` — **never `--no-build`** when new test files were added. Frontend: `npm test`, `npm run typecheck`, `npm run lint`, `npm run build` from `src/frontend` (3 pre-existing lint warnings in admin tab files are known).
- Git: stage files explicitly (never `git add -A`); never commit `.claude/settings.local.json`, `src/frontend/src/App.test.tsx`, `src/snoopy.microservice/ApiDocumentation.xml`; commit message = subject + ≤2 body lines, never starting or ending with `@`, via POSIX heredoc `git commit -F -`; **NEVER push**.

---

### Task 1: Backend — `MailMessageSummary.To`

The Drafts list must show "To: …" instead of the sender. The envelope already carries the recipients; transcribe them.

**Files:**
- Modify: `src/snoopy.microservice/Models/Mail/MailMessageSummary.cs`
- Modify: `src/snoopy.microservice/Services/ImapSession.cs` (the `FillSummary` mapping, ~line 692 — also change its visibility to `internal` so the mapping is testable)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapSessionTests.cs`

**Interfaces:**
- Produces: `MailMessageSummary.To : List<MailAddressInfo>` (serialised `to` on the wire), filled for every list row and search hit.

- [ ] **Step 1: Write the failing tests**

In `ImapSessionTests.cs` (Moq can mock MailKit's `IMessageSummary`; `Envelope` is a concrete class whose `To` list is mutable):

```csharp
[Fact]
public void FillSummary_TranscribesTheEnvelopeRecipients()
{
    var envelope = new Envelope();
    envelope.To.Add(new MailboxAddress("Bob", "bob@ext.example"));
    envelope.To.Add(new MailboxAddress(string.Empty, "carol@ext.example"));
    var item = new Mock<IMessageSummary>();
    item.SetupGet(i => i.UniqueId).Returns(new UniqueId(7));
    item.SetupGet(i => i.Envelope).Returns(envelope);

    var summary = ImapSession.FillSummary(new MailMessageSummary(), item.Object);

    Assert.Equal(2, summary.To.Count);
    Assert.Equal("Bob", summary.To[0].Name);
    Assert.Equal("bob@ext.example", summary.To[0].Address);
    Assert.Equal("carol@ext.example", summary.To[1].Address);
}

[Fact]
public void FillSummary_LeavesToEmptyWithoutAnEnvelope()
{
    var item = new Mock<IMessageSummary>();
    item.SetupGet(i => i.UniqueId).Returns(new UniqueId(7));
    item.SetupGet(i => i.Envelope).Returns((Envelope?)null);

    var summary = ImapSession.FillSummary(new MailMessageSummary(), item.Object);

    Assert.Empty(summary.To);
}
```

- [ ] **Step 2: Run to verify RED** — `dotnet test --filter FillSummary` from `src/snoopy.microservice`. Expected: compile failure (`To` and `FillSummary` visibility don't exist yet).

- [ ] **Step 3: Implement**

`MailMessageSummary.cs`, after `FromAddress`:

```csharp
/// <summary>Recipients from the envelope — the drafts folder lists "To:" instead of the sender.</summary>
public List<MailAddressInfo> To { get; set; } = [];
```

`ImapSession.cs`: change `private static T FillSummary<T>` to `internal static T FillSummary<T>` and add inside it:

```csharp
summary.To = ToAddressInfos(item.Envelope?.To);
```

- [ ] **Step 4: Run to verify GREEN** — the two new tests pass; then the full suite: `dotnet test`. All green.

- [ ] **Step 5: Commit** — `git add` the three files, message: `Webmail 2c3a: summaries carry the envelope recipients`.

---

### Task 2: Backend — extract `OutgoingMessageFactory` from `MailSender`

Pure refactor: the From-ownership check, staged-id resolution and MIME construction move to a shared factory so Send and the coming DraftSaver cannot drift. **No behavior change — the existing `MailSenderTests` suite is the gate and must stay green unmodified** (except constructor wiring in its fixture if it news up `MailSender` directly).

**Files:**
- Create: `src/snoopy.microservice/Services/IOutgoingMessageFactory.cs`
- Create: `src/snoopy.microservice/Services/OutgoingMessageFactory.cs`
- Modify: `src/snoopy.microservice/Services/MailSender.cs`
- Modify: `src/snoopy.microservice/Services/IMailSender.cs` (constants re-pointed)
- Modify: `Program.cs` (DI registration, beside the existing `IMailSender` registration)
- Test: existing `snoopy.microservice.Tests/Services/MailSenderTests.cs` (fixture wiring only)

**Interfaces:**
- Produces:

```csharp
namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Builds the outgoing MimeMessage Send and Drafts share: validated From, staged attachments
/// resolved, outbound-sanitised body with staged URLs rewritten to cid, safe threading headers.
/// </summary>
internal interface IOutgoingMessageFactory
{
    const string ForbiddenFrom = "from_not_owned";
    const string UnknownAttachment = "unknown_attachment";

    Task<Result<MimeMessage>> CreateAsync(
        User user, SendMessageRequest request, CancellationToken cancellationToken);
}
```

- `IMailSender`'s existing constants become aliases so `MailController` keeps compiling untouched:

```csharp
const string ForbiddenFrom = IOutgoingMessageFactory.ForbiddenFrom;
const string UnknownAttachment = IOutgoingMessageFactory.UnknownAttachment;
```

(If the current constants hold different literal values, KEEP the current literals — move them to the factory and alias from `IMailSender`; the controller's equality checks must keep matching.)

- [ ] **Step 1: Create the factory**

`OutgoingMessageFactory.cs` — move from `MailSender`, verbatim, the bodies of: the From resolution block of `SendAsync` (lines 62–75), the staged-open loop (77–83), `BuildMessageAsync` and its FileNotFound catch, `DomainOf`, `ApplyThreadingHeaders`, `LoadIdentitiesAsync`, `AddAddresses`. Shape:

```csharp
internal sealed class OutgoingMessageFactory(
    IUsersRepository users,
    IAliasesRepository aliases,
    ISendingIdentityStore identities,
    IOutgoingMailSanitizer sanitizer,
    IStagedAttachmentStore staged,
    ILogger<OutgoingMessageFactory> logger) : IOutgoingMessageFactory
{
    public async Task<Result<MimeMessage>> CreateAsync(
        User user, SendMessageRequest request, CancellationToken cancellationToken)
    {
        var userId = user.WebmailUid;
        // 1) From resolution + ownership (moved code; failure -> IOutgoingMessageFactory.ForbiddenFrom)
        // 2) staged-open loop (failure -> IOutgoingMessageFactory.UnknownAttachment)
        // 3) try { return Result.Success(await BuildMessageAsync(...)); }
        //    catch (FileNotFoundException/DirectoryNotFoundException) -> UnknownAttachment (moved catch)
    }
    // moved private helpers here, unchanged
}
```

The moved code's XML docs and comments travel with it. Primary-constructor style (project rule), so `_field` prefixes drop to the parameter names.

- [ ] **Step 2: Slim `MailSender`**

`MailSender` keeps: `SendAsync` orchestration, SMTP session, `AppendToSentAsync`, the post-send staged delete. Its dependency list shrinks to `IOutgoingMessageFactory factory, IStagedAttachmentStore staged, ISmtpConnectionFactory smtpFactory, IMailFolderRepository folders, IFolderRoleStore roles, IMailMessageRepository messages, ILogger<MailSender> logger` (convert to a primary constructor while touching it). `SendAsync` becomes:

```csharp
var built = await factory.CreateAsync(user, request, cancellationToken);
if (built.IsFailure) return Result.Failure<SendMessageResult>(built.Error);
var message = built.Value;
// ... unchanged SMTP + AppendToSentAsync + staged delete ...
```

- [ ] **Step 3: Register in DI** — in `Program.cs`, next to the `IMailSender` registration: `builder.Services.AddScoped<IOutgoingMessageFactory, OutgoingMessageFactory>();` (match the lifetime `IMailSender` uses).

- [ ] **Step 4: Fix the test fixture** — `MailSenderTests` constructs `MailSender` with mocks; it now also needs a real `OutgoingMessageFactory` built from the same mocks (NOT a mock of the factory — the tests assert on the built message's wire form, threading/CRLF guarantees included, and must keep exercising the real construction). Change only the arrange wiring; **no assertion changes**.

- [ ] **Step 5: Verify** — `dotnet test` full suite green, `dotnet build -c Release` zero warnings.

- [ ] **Step 6: Commit** — message: `Webmail 2c3a: extract OutgoingMessageFactory from MailSender`.

---

### Task 3: Backend — `SaveDraftAsync` on the IMAP session and repository

**Files:**
- Modify: `src/snoopy.microservice/Services/IImapSession.cs`
- Modify: `src/snoopy.microservice/Services/ImapSession.cs` (near `AppendAsync`, ~line 405)
- Modify: `src/snoopy.microservice/Repositories/IMailMessageRepository.cs`
- Modify: `src/snoopy.microservice/Repositories/MailMessageRepository.cs`
- Test: `snoopy.microservice.Tests/Repositories/MailMessageRepositoryTests.cs` (follow the file's existing mocked-factory/session pattern; create the file if none exists, following the sibling repository tests)

**Interfaces:**
- Produces (both layers):

```csharp
/// <summary>Appends a draft (\Draft \Seen) and expunges the version it replaces. Returns the new UID.</summary>
Task<Result<uint>> SaveDraftAsync(string folderPath, MimeMessage message, uint? replaceUid, CancellationToken cancellationToken);
// repository adds (User user, string password, ...) in front, like AppendAsync
```

- [ ] **Step 1: Write the failing repository tests** — mock `IImapConnectionFactory`/`IImapSession` exactly as the existing repository tests do:

```csharp
[Fact]
public async Task SaveDraft_DelegatesToTheSession()
{
    _session.Setup(s => s.SaveDraftAsync("Drafts", It.IsAny<MimeMessage>(), 41u, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Success(42u));

    var result = await _repository.SaveDraftAsync(_user, "pw", "Drafts", new MimeMessage(), 41u, CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Equal(42u, result.Value);
}

[Fact]
public async Task SaveDraft_FailsWhenTheSessionCannotOpen()
{
    _factory.Setup(f => f.OpenAsync(_user.Email, "pw", It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Failure<IImapSession>("boom"));

    var result = await _repository.SaveDraftAsync(_user, "pw", "Drafts", new MimeMessage(), null, CancellationToken.None);

    Assert.True(result.IsFailure);
}
```

- [ ] **Step 2: RED** — compile failure (method absent).

- [ ] **Step 3: Implement**

`ImapSession.SaveDraftAsync` (mirror `AppendAsync`'s error envelope):

```csharp
public async Task<Result<uint>> SaveDraftAsync(
    string folderPath, MimeMessage message, uint? replaceUid, CancellationToken cancellationToken)
{
    ThrowIfDisposed();

    try
    {
        var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
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
                _logger.LogWarning(ex, "Could not remove replaced draft {Uid} in {Folder}", previous, folderPath);
            }
        }

        return Result.Success(appended.Value.Id);
    }
    catch (FolderNotFoundException)
    {
        return Result.Failure<uint>(FolderNotFound);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to save a draft in {Folder}", folderPath);
        return Result.Failure<uint>("Unable to save the draft");
    }
}
```

(UID EXPUNGE needs UIDPLUS — guaranteed here, since a non-null APPENDUID already proved it.)

`MailMessageRepository.SaveDraftAsync` — same open-session-and-delegate shape as `AppendAsync` (lines 111–120).

- [ ] **Step 4: GREEN** — new tests pass, full `dotnet test` green.

- [ ] **Step 5: Commit** — message: `Webmail 2c3a: draft APPEND with replace on the IMAP session`.

---

### Task 4: Backend — `DraftSaver` and the request/response models

**Files:**
- Modify: `src/snoopy.microservice/Models/Mail/SendMessageRequest.cs` (drop `sealed` so the draft request can extend it)
- Create: `src/snoopy.microservice/Models/Mail/SaveDraftRequest.cs`
- Create: `src/snoopy.microservice/Models/Mail/SavedDraft.cs`
- Create: `src/snoopy.microservice/Services/IDraftSaver.cs`
- Create: `src/snoopy.microservice/Services/DraftSaver.cs`
- Modify: `Program.cs` (register beside `IMailSender`)
- Test: `snoopy.microservice.Tests/Services/DraftSaverTests.cs`

**Interfaces:**
- Consumes: `IOutgoingMessageFactory.CreateAsync` (Task 2), `IMailMessageRepository.SaveDraftAsync` (Task 3), `FolderRoleResolver.Resolve` (existing).
- Produces:

```csharp
/// <summary>A draft save: the send shape plus the previous version this one replaces.</summary>
public record SaveDraftRequest : SendMessageRequest
{
    /// <summary>UID of the superseded version — expunged once the new one is in place.</summary>
    public uint? ReplaceUid { get; init; }
}

public sealed record SavedDraft(uint Uid, string FolderPath);

internal interface IDraftSaver
{
    const string NoDraftsFolder = "no_drafts_folder";
    Task<Result<SavedDraft>> SaveAsync(
        User user, string password, SaveDraftRequest request, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write the failing tests** — fixture mirrors `MailSenderTests` (same mocks; real `OutgoingMessageFactory` where the message content matters):

```csharp
// 1. SaveDraft_AppendsToTheDraftsRoleFolder — tree with a "Drafts" node carrying
//    AttributeRole = "drafts"; repository mock expects SaveDraftAsync(_user, "pw", "Drafts",
//    It.IsAny<MimeMessage>(), null, ...) and returns Success(7u). Assert result.Value is
//    { Uid: 7, FolderPath: "Drafts" }.
// 2. SaveDraft_PassesTheReplaceUid — request with ReplaceUid = 41; verify the repository
//    received 41u.
// 3. SaveDraft_FailsWithoutADraftsFolder — tree with no drafts role anywhere; assert
//    IsFailure with IDraftSaver.NoDraftsFolder, and the repository was never called.
// 4. SaveDraft_AcceptsAnEmptyDraft — request with no recipients, empty subject, empty body:
//    IsSuccess (the factory builds a legal empty message).
// 5. SaveDraft_RefusesAForeignFrom — FromAddress the account does not own: IsFailure with
//    IOutgoingMessageFactory.ForbiddenFrom (surfaces through).
// 6. SaveDraft_KeepsStagedFilesAfterTheSave — a request with one staged attachment id;
//    assert the staged store's Delete was NEVER called (the composer still holds them).
// 7. SaveDraft_DegradesRoleOverridesToServerFlags — roles store GetAsync throws; the tree's
//    SPECIAL-USE drafts folder is still used and the save succeeds.
```

Write all seven as real tests with the fixture's mocks (copy `MailSenderTests`' arrange helpers for user/aliases/identities/tree).

- [ ] **Step 2: RED** — compile failure.

- [ ] **Step 3: Implement**

```csharp
internal sealed class DraftSaver(
    IOutgoingMessageFactory factory,
    IMailFolderRepository folders,
    IFolderRoleStore roles,
    IMailMessageRepository messages,
    ILogger<DraftSaver> logger) : IDraftSaver
{
    public async Task<Result<SavedDraft>> SaveAsync(
        User user, string password, SaveDraftRequest request, CancellationToken cancellationToken)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));

        var built = await factory.CreateAsync(user, request, cancellationToken);
        if (built.IsFailure) return Result.Failure<SavedDraft>(built.Error);

        var tree = await folders.GetTreeAsync(user, password, cancellationToken);
        if (tree.IsFailure) return Result.Failure<SavedDraft>(tree.Error);

        // A preferences outage must not block a save the SPECIAL-USE flags can already place.
        IReadOnlyList<FolderRoleOverride> overrides;
        try
        {
            overrides = await roles.GetAsync(user.WebmailUid, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Role overrides unavailable for {UserId}: using server flags", user.WebmailUid);
            overrides = [];
        }

        var drafts = FolderRoleResolver.Resolve(tree.Value, overrides).Roles
            .FirstOrDefault(r => r.Role == "drafts" && r.FolderPath != null);
        // Unlike the Sent copy this APPEND is the whole point of the request: no folder is a failure.
        if (drafts == null) return Result.Failure<SavedDraft>(IDraftSaver.NoDraftsFolder);

        var saved = await messages.SaveDraftAsync(
            user, password, drafts.FolderPath!, built.Value, request.ReplaceUid, cancellationToken);
        if (saved.IsFailure) return Result.Failure<SavedDraft>(saved.Error);

        // Staged files stay: the composer is still open on them for the next save or the send.
        return Result.Success(new SavedDraft(saved.Value, drafts.FolderPath!));
    }
}
```

DI: `builder.Services.AddScoped<IDraftSaver, DraftSaver>();`

- [ ] **Step 4: GREEN** — new tests + full suite.

- [ ] **Step 5: Commit** — message: `Webmail 2c3a: DraftSaver files the built message under the drafts role`.

---

### Task 5: Backend — `POST /api/Mail/Drafts` and `POST /api/Mail/Drafts/Open`

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/OpenDraftRequest.cs`
- Create: `src/snoopy.microservice/Models/Mail/OpenedDraft.cs`
- Modify: `src/snoopy.microservice/Controllers/MailController.cs` (inject `IDraftSaver`, two actions after `PrepareQuote`)
- Test: the controller test file holding the `SendMessage`/`PrepareQuote` tests (find it; follow its fixture)

**Interfaces:**
- Consumes: `IDraftSaver.SaveAsync` (T4), `IMailMessageRepository.GetMimeMessageAsync` + `IQuotePreparer.PrepareAsync(…, QuotePurpose.EditAsNew, …)` (existing).
- Produces (wire shapes the frontend consumes in T6):

```csharp
public sealed record OpenDraftRequest(string Folder, uint Uid);

/// <summary>Everything the composer needs to resume a draft: envelope, editable body, re-staged parts.</summary>
public sealed record OpenedDraft(
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string Subject,
    string? FromAddress,
    string HtmlBody,
    IReadOnlyList<StagedAttachmentInfo> Attachments,
    string? InReplyTo,
    IReadOnlyList<string> References);
```

- [ ] **Step 1: Write the failing controller tests** (same authenticated-context helpers as the Send/PrepareQuote tests):

```csharp
// SaveDraft:
// 1. SaveDraft_Returns200WithTheSavedLocation — saver mock answers SavedDraft(7, "Drafts");
//    assert Ok payload.
// 2. SaveDraft_AcceptsNoRecipient — request with empty To/Cc/Bcc reaches the saver (no
//    "at least one recipient" gate — that rule is Send's, not Drafts').
// 3. SaveDraft_RejectsAMalformedRecipient — To = ["not an address"] → BadRequestObjectResult,
//    saver never called.
// 4. SaveDraft_RejectsAForeignFrom — saver answers Failure(IOutgoingMessageFactory.ForbiddenFrom)
//    → BadRequestObjectResult naming the address.
// 5. SaveDraft_RejectsAnUnknownStagedId — Failure(IOutgoingMessageFactory.UnknownAttachment)
//    → BadRequestObjectResult.
// 6. SaveDraft_Returns502WithoutADraftsFolder — Failure(IDraftSaver.NoDraftsFolder) → 502 with
//    a message telling the user to assign the drafts role.
// 7. SaveDraft_Returns401WithoutCredentials — credentials store failure → Unauthorized.
// OpenDraft:
// 8. OpenDraft_ReturnsTheEnvelopeAndPreparedBody — GetMimeMessageAsync answers a built
//    MimeMessage (To: two addresses, Cc: one, Subject, In-Reply-To + References set via
//    MimeUtils.ParseMessageId, From: "Me <me@weesky.be>"); quote preparer answers a
//    PreparedQuote("<p>Hi</p>", one staged info). Assert every OpenedDraft field, references
//    oldest-first, bare ids, FromAddress == "me@weesky.be".
// 9. OpenDraft_Returns404ForAMissingUid — repository answers Failure(ImapSession.MessageNotFound)
//    → NotFoundObjectResult.
// 10. OpenDraft_Returns400WhenStagingFails — preparer answers Failure("cap") → BadRequestObjectResult.
// 11. OpenDraft_RequiresAFolder — empty folder → BadRequestObjectResult, repository never called.
```

- [ ] **Step 2: RED.**

- [ ] **Step 3: Implement the actions**

`SaveDraft` — mirror `SendMessage`'s normalisation and address validation minus the recipient-count gate:

```csharp
/// <summary>
/// Saves the composer's content as a draft in the drafts-role folder (\Draft \Seen), replacing
/// the previous version when replaceUid names one. An empty or recipient-less draft is valid;
/// the message itself is built by the same pipeline as Send, so threading and attachments
/// survive a save/resume round trip. Attachments live in the stored message — the staged
/// files remain for the still-open composer and expire on their own.
/// </summary>
/// <response code="200">Saved; the new UID and the folder it landed in</response>
/// <response code="400">An invalid address, a fromAddress the account does not own, or a staged id no longer available</response>
/// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
/// <response code="502">No folder holds the drafts role, or the mail server refused the save</response>
[HttpPost("Drafts")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status502BadGateway)]
public async Task<ActionResult<SavedDraft>> SaveDraft(SaveDraftRequest request, CancellationToken cancellationToken)
{
    if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));

    request = request with { To = request.To ?? [], Cc = request.Cc ?? [], Bcc = request.Bcc ?? [], References = request.References ?? [] };

    foreach (var address in request.To.Concat(request.Cc).Concat(request.Bcc))
    {
        if (string.IsNullOrWhiteSpace(address) || !MailboxAddress.TryParse(RecipientAddressParser.Options, address, out _))
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe($"\"{address}\" is not a valid email address"));
    }

    if (!string.IsNullOrWhiteSpace(request.FromAddress))
    {
        if (!MailboxAddress.TryParse(RecipientAddressParser.Options, request.FromAddress, out var from))
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(
                $"\"{request.FromAddress}\" is not a valid email address"));
        request = request with { FromAddress = from.Address };
    }

    var password = _credentials.Retrieve(Request);
    if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

    var result = await _drafts.SaveAsync(AuthenticatedUser, password.Value, request, cancellationToken);

    if (result.IsFailure && result.Error == IOutgoingMessageFactory.UnknownAttachment)
        return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(
            "An attachment is no longer available; remove it and attach it again"));
    if (result.IsFailure && result.Error == IOutgoingMessageFactory.ForbiddenFrom)
        return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(
            $"Sending from \"{request.FromAddress}\" is not allowed on this account"));
    if (result.IsFailure && result.Error == IDraftSaver.NoDraftsFolder)
        return StatusCode(StatusCodes.Status502BadGateway, ResultEnveloppe.CreateErrorEnveloppe(
            "This mailbox has no drafts folder. Assign the drafts role in Settings > Folders list."));

    return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
}
```

`OpenDraft` — `GetMimeMessageAsync` + `PrepareAsync(EditAsNew)` exactly like `PrepareQuote` (same 404/502/400 mapping), then map:

```csharp
private static OpenedDraft ToOpenedDraft(MimeMessage message, PreparedQuote prepared) =>
    new(
        Addresses(message.To), Addresses(message.Cc), Addresses(message.Bcc),
        message.Subject ?? string.Empty,
        message.From?.Mailboxes?.FirstOrDefault()?.Address,
        prepared.QuotableHtml,
        prepared.Attachments,
        string.IsNullOrWhiteSpace(message.InReplyTo) ? null : message.InReplyTo,
        message.References?.ToList() ?? []);

private static List<string> Addresses(InternetAddressList? list) =>
    list?.Mailboxes?.Select(m => m.Address).ToList() ?? [];
```

- [ ] **Step 4: GREEN** — new tests + full suite, `dotnet build -c Release` zero warnings.

- [ ] **Step 5: Commit** — message: `Webmail 2c3a: draft save and open endpoints`.

---

### Task 6: Frontend — API methods, types, query hooks

**Files:**
- Modify: `src/frontend/src/api.js` (beside `prepareQuote`)
- Modify: `src/frontend/src/modules/mail/api/mailTypes.ts`
- Modify: `src/frontend/src/modules/mail/queries.ts`
- Test: `src/frontend/src/api.test.js`, plus fixture updates wherever `MailMessageSummary` literals now miss `to` (typecheck names them)

**Interfaces:**
- Produces:

```ts
// api.js
saveDraft: (payload) => request('POST', '/api/Mail/Drafts', payload),
openDraft: (folder, uid) => request('POST', '/api/Mail/Drafts/Open', { folder, uid }),

// mailTypes.ts
export interface SavedDraft { uid: number; folderPath: string }
/** Everything the composer needs to resume a draft — the seed's raw material. */
export interface OpenedDraft {
  to: string[]
  cc: string[]
  bcc: string[]
  subject: string
  fromAddress: string | null
  htmlBody: string
  attachments: StagedAttachmentInfo[]
  inReplyTo: string | null
  references: string[]
}
// MailMessageSummary gains (after fromAddress):
/** Envelope recipients — the drafts folder lists "To:" instead of the sender. */
to: MailAddressInfo[]

// queries.ts
export type SaveDraftArgs = SendMessageArgs & { replaceUid?: number }
export function useSaveDraft(): UseMutationResult<SavedDraft, Error, SaveDraftArgs>
export function useOpenDraft(): // mutation, args { folder: string; uid: number } -> OpenedDraft
```

- [ ] **Step 1: Failing tests** — in `api.test.js`, follow the file's fetch-mock pattern:

```js
// saveDraft POSTs the payload to /api/Mail/Drafts and resolves the body
// openDraft POSTs { folder, uid } to /api/Mail/Drafts/Open
```

- [ ] **Step 2: RED**, then implement the two `api.js` methods and the types.

- [ ] **Step 3: Hooks** in `queries.ts`:

```ts
export type SaveDraftArgs = SendMessageArgs & { replaceUid?: number }

/** Files the draft under the drafts role; each success replaces the version before it. */
export function useSaveDraft() {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: (args: SaveDraftArgs) => api.saveDraft(args) as Promise<SavedDraft>,
    onSuccess: (saved) => {
      queryClient.invalidateQueries({ queryKey: mailKeys.folders(accountId) })
      queryClient.invalidateQueries({ queryKey: mailKeys.messagesIn(accountId, saved.folderPath) })
      queryClient.invalidateQueries({ queryKey: mailKeys.messageStreamIn(accountId, saved.folderPath) })
    },
  })
}

/**
 * Opens a draft for the composer. A mutation, not a query: every call re-stages the draft's
 * parts (a side effect with a TTL), so the result must never be cached or replayed.
 */
export function useOpenDraft() {
  return useMutation({
    mutationFn: (args: { folder: string; uid: number }) =>
      api.openDraft(args.folder, args.uid) as Promise<OpenedDraft>,
  })
}
```

- [ ] **Step 4: Make `to` required on `MailMessageSummary`** and run `npm run typecheck`; add `to: []` to every summary fixture it names.

- [ ] **Step 5: GREEN** — `npm test`, `npm run typecheck`, `npm run lint` clean.

- [ ] **Step 6: Commit** — message: `Webmail 2c3a: draft API, types and query hooks`.

---

### Task 7: Frontend — `draft` compose action and `buildDraftSeed`

**Files:**
- Modify: `src/frontend/src/modules/mail/compose/composeSeed.ts`
- Test: `src/frontend/src/modules/mail/compose/composeSeed.test.ts`

**Interfaces:**
- Produces:

```ts
export type ComposeAction = 'reply' | 'replyAll' | 'forward' | 'editAsNew' | 'draft'
export interface DraftRef { folderPath: string; uid: number }
// ComposeSeed gains:
/** Set when the composer is editing an existing draft — the version a save replaces. */
draftRef: DraftRef | null

export function buildDraftSeed(
  opened: OpenedDraft, identities: SendingIdentity[], ref: DraftRef,
): ComposeSeed
```

- [ ] **Step 1: Failing tests** in `composeSeed.test.ts`:

```ts
describe('buildDraftSeed', () => {
  const opened: OpenedDraft = {
    to: ['bob@ext.example'], cc: ['carol@ext.example'], bcc: [],
    subject: 'WIP', fromAddress: 'sales@weesky.be',
    htmlBody: '<p>hello <img src="/api/Mail/Attachments/a1/content"></p>',
    attachments: [
      { id: 'a1', fileName: 'logo.png', size: 5, contentType: 'image/png', contentId: 'logo@mail' },
      { id: 'a2', fileName: 'doc.pdf', size: 9, contentType: 'application/pdf', contentId: null },
    ],
    inReplyTo: 'msg1@ext.example', references: ['msg0@ext.example', 'msg1@ext.example'],
  }
  const ref = { folderPath: 'Drafts', uid: 41 }

  it('carries the envelope, threading and draftRef', () => {
    const seed = buildDraftSeed(opened, [identity('sales@weesky.be')], ref)
    expect(seed.action).toBe('draft')
    expect(seed.to).toEqual(['bob@ext.example'])
    expect(seed.subject).toBe('WIP')
    expect(seed.inReplyTo).toBe('msg1@ext.example')
    expect(seed.references).toEqual(['msg0@ext.example', 'msg1@ext.example'])
    expect(seed.draftRef).toEqual(ref)
    expect(seed.attachments).toHaveLength(2)
  })

  it('absolutizes the staged URLs in the body', () => {
    const seed = buildDraftSeed(opened, [], ref)
    expect(seed.html).toContain(stagedAttachmentUrl('a1'))
  })

  it('keeps the draft From only when a usable identity owns it', () => {
    expect(buildDraftSeed(opened, [identity('sales@weesky.be')], ref).fromAddress).toBe('sales@weesky.be')
    expect(buildDraftSeed(opened, [identity('sales@weesky.be', { stale: true })], ref).fromAddress).toBeNull()
    expect(buildDraftSeed(opened, [identity('other@weesky.be')], ref).fromAddress).toBeNull()
    // Case differences must not lose the choice: an IMAP client may have stored it capitalised.
    expect(buildDraftSeed({ ...opened, fromAddress: 'Sales@weesky.be' }, [identity('sales@weesky.be')], ref)
      .fromAddress).toBe('sales@weesky.be')
  })
})
```

(Reuse the file's existing `identity()` helper; extend it with a `stale` override if it lacks one.)

- [ ] **Step 2: RED** (also: adding `draftRef` to `ComposeSeed` as a required field breaks `buildComposeSeed`'s three return branches — expected).

- [ ] **Step 3: Implement** — add `draftRef: null` to each `buildComposeSeed` branch, then:

```ts
/** Turns an opened draft into a composer seed. Pure. */
export function buildDraftSeed(
  opened: OpenedDraft, identities: SendingIdentity[], ref: DraftRef,
): ComposeSeed {
  const usable = identities.filter(i => !i.stale)
  const owned = opened.fromAddress
    ? usable.find(i => i.address.toLowerCase() === opened.fromAddress!.toLowerCase())
    : undefined
  return {
    action: 'draft',
    to: opened.to, cc: opened.cc, bcc: opened.bcc,
    subject: opened.subject,
    html: absolutizeStagedUrls(opened.htmlBody, opened.attachments.map(a => a.id)),
    fromAddress: owned?.address ?? null,
    attachments: opened.attachments,
    inReplyTo: opened.inReplyTo,
    references: opened.references,
    draftRef: ref,
  }
}
```

- [ ] **Step 4: GREEN** — focused file, then `npm test` + `npm run typecheck` (other files now missing `draftRef` in seed literals get `draftRef: null`).

- [ ] **Step 5: Commit** — message: `Webmail 2c3a: draft compose action and seed builder`.

---

### Task 8: Frontend — ComposeView: Save draft, changed-since-save dirty, 3-choice leave dialog

The composer's `dirty` changes meaning, as its own comment (ComposeView.tsx:72-74) and the frontend CLAUDE.md planned: from "the form is non-empty" to "**changed since open or the last save** (and non-empty)". A From-only edit to a loaded draft must now count. The staged-inline cleanup moves into `useStagedAttachments` (the 2c2b deferred item).

**Files:**
- Modify: `src/frontend/src/modules/mail/compose/ComposeView.tsx`
- Modify: `src/frontend/src/modules/mail/compose/useStagedAttachments.ts`
- Test: `ComposeView.test.tsx`, `useStagedAttachments.test.ts` (or the hook's existing coverage site)
- Modify: the mail stylesheet (find where `.compose-header`/`.compose-send` live) for the Save-draft button spacing if needed

**Interfaces:**
- Consumes: `useSaveDraft`/`SaveDraftArgs` (T6), `DraftRef`/`'draft'` action (T7), `useDeleteMessages` (existing).
- Produces: `useStagedAttachments(initial, inlineIds: string[] = [])` — `discardAll` now also deletes the inline ids; everything else unchanged.

- [ ] **Step 1: Failing tests** in `ComposeView.test.tsx` (follow the file's mount/mock patterns; mock `api.saveDraft`):

```ts
// 1. "saves a draft and replaces it on the next save": type a subject, click "Save draft"
//    → api.saveDraft called without replaceUid; resolves { uid: 7, folderPath: 'Drafts' };
//    toast "Draft saved". Type more, save again → second call carries replaceUid: 7.
// 2. "a draft seed opens clean and a From-only change dirties it": mount with a seed
//    { action: 'draft', draftRef: { folderPath: 'Drafts', uid: 41 }, ... } → navigating away
//    triggers no dialog. Change the From (or any field), navigate → the dialog appears.
// 3. "the leave dialog offers Save draft / Discard / Keep editing": dirty form, navigate;
//    assert the three buttons. "Save draft" → api.saveDraft called, then navigation proceeds.
//    "Keep editing" → dialog closes, still on /mail/compose. "Discard" → navigation proceeds,
//    staged ids (tray + inline) deleted.
// 4. "sending a resumed draft deletes it": seed with draftRef; send succeeds → 
//    api.deleteMessages called with ('Drafts', [41]).
// 5. "saving a resumed draft targets its own uid": seed with draftRef uid 41 → first save
//    carries replaceUid: 41.
```

For `useStagedAttachments`: `discardAll` with `inlineIds: ['i1']` also calls `api.deleteAttachment('i1')`.

- [ ] **Step 2: RED.**

- [ ] **Step 3: Implement in `ComposeView.tsx`**

- `TITLES` gains `draft: 'Draft'`.
- Draft tracking + changed-since-save:

```ts
const saveDraftMutation = useSaveDraft()
const deleteDraft = useDeleteMessages()
const [draftRef, setDraftRef] = useState(seed?.draftRef ?? null)

// "Changed since open or the last save". A resumed draft opens clean — its content is
// already filed; any other seed opens changed, because its content exists nowhere else.
const [changed, setChanged] = useState(() => Boolean(seed) && seed?.action !== 'draft')
```

- `dirty` becomes `changed && (to.length > 0 || cc.length > 0 || bcc.length > 0 || subject !== '' || bodyTouched || attachments.items.length > 0)`; `markDirty` also sets `setChanged(true)` (and keeps the refs immediate: `dirtyRef.current = true`). Update the stale comment block above it.
- From selection wraps: `const changeFrom = useCallback((v: string | null) => { markDirty(); setFromAddress(v) }, [markDirty])` — passed to `IdentitySelect`.
- Extract the send payload into one builder both actions share:

```ts
const buildPayload = () => ({
  to, cc, bcc, subject, htmlBody: relativizeStagedUrls(editor?.getHTML() ?? ''),
  attachmentIds: [...inlineIds, ...attachments.ids],
  fromAddress: effectiveFrom ?? undefined,
  inReplyTo: seed?.inReplyTo ?? undefined,
  references: seed?.references && seed.references.length > 0 ? seed.references : undefined,
})
```

- Save:

```ts
const canSaveDraft = allValid && !attachments.uploading && !saveDraftMutation.isPending
function saveDraft(onSaved?: () => void) {
  saveDraftMutation.mutate(
    { ...buildPayload(), replaceUid: draftRef?.uid },
    {
      onSuccess: (saved) => {
        setDraftRef({ folderPath: saved.folderPath, uid: saved.uid })
        setChanged(false)
        dirtyRef.current = false
        onNotify('Draft saved')
        onSaved?.()
      },
      onError: (error: Error) => onNotify(error.message || 'Could not save the draft', 'error'),
    },
  )
}
```

- Header button between Send and ✕: `<button type="button" className="btn btn-ghost" disabled={!canSaveDraft} onClick={() => saveDraft()}>{saveDraftMutation.isPending ? 'Saving…' : 'Save draft'}</button>`
- Send `onSuccess` gains, before `leave()`: `if (draftRef) deleteDraft.mutate({ folderPath: draftRef.folderPath, uids: [draftRef.uid] })`
- The blocker dialog becomes three-way:

```tsx
<span className="modal-title">Save this draft?</span>
<p>Your message has unsaved changes.</p>
<div className="folder-pick-actions">
  <button type="button" className="btn btn-ghost" onClick={() => blocker.reset?.()}>Keep editing</button>
  <button type="button" className="btn btn-ghost"
    onClick={() => {
      // The staged copies are scratch either way: a saved draft holds its own bytes in IMAP.
      attachments.discardAll()
      leavingRef.current = true
      blocker.proceed?.()
    }}>
    Discard
  </button>
  <button type="button" className="btn btn-primary" disabled={!canSaveDraft}
    onClick={() => saveDraft(() => {
      attachments.discardAll()
      leavingRef.current = true
      blocker.proceed?.()
    })}>
    Save draft
  </button>
</div>
```

- `useStagedAttachments(seedTray, inlineIds)` — the hook change (Step 4) makes `discardAll` cover the inline ids, so the old direct `api.deleteAttachment` loop in the dialog goes away.

- [ ] **Step 4: Implement in `useStagedAttachments.ts`**

```ts
export function useStagedAttachments(
  initial: { id: string; fileName: string; size: number }[] = [],
  inlineIds: string[] = [],
) {
  const inlineRef = useRef(inlineIds)
  inlineRef.current = inlineIds
  // ...
  const discardAll = useCallback(() => {
    for (const id of inlineRef.current) api.deleteAttachment(id).catch(() => { /* sweeper's problem now */ })
    for (const item of itemsRef.current) {
      if (item.id) api.deleteAttachment(item.id).catch(() => { /* sweeper's problem now */ })
    }
    apply(() => [])
  }, [apply])
```

- [ ] **Step 5: GREEN** — focused files, then full `npm test`, `npm run typecheck`, `npm run lint`, `npm run build`.

- [ ] **Step 6: Commit** — message: `Webmail 2c3a: Save draft, changed-since-save dirty, three-way leave dialog`.

---

### Task 9: Frontend — the Drafts folder opens into the composer and lists recipients

**Files:**
- Modify: `src/frontend/src/modules/mail/MailLayout.tsx` (`selectMessage`, ~line 96)
- Modify: `src/frontend/src/modules/mail/list/MessageList.tsx` (row sender text, both skins)
- Modify: the mail stylesheet holding `.message-row` styles (one small class)
- Test: `MessageList.test.tsx` (or the file covering rows) + the suite covering `MailLayout` if one exists (else cover the draft-click path through `MessageList`'s `onSelect` contract and say so in the report)

**Interfaces:**
- Consumes: `useOpenDraft` (T6), `buildDraftSeed` (T7), `message.to` (T6), the `folderRole` MailLayout already derives and passes to `MessageList`.

- [ ] **Step 1: Failing tests**

```ts
// MessageList, folderRole="drafts":
// 1. rows show "Draft" and the recipients' labels ("Bob, carol@ext.example") instead of the sender
// 2. a row whose to is empty shows "(no recipient)"
// 3. any other folderRole still shows the sender (regression pin)
```

- [ ] **Step 2: RED.**

- [ ] **Step 3: Implement**

`MessageList.tsx` — where the row computes `const from = message.fromName || message.fromAddress` (~line 315):

```ts
const drafts = folderRole === 'drafts'
const from = drafts
  ? (message.to.length > 0
      ? message.to.map(a => a.name || a.address).join(', ')
      : '(no recipient)')
  : (message.fromName || message.fromAddress)
```

and render the marker before the sender element in **both** skins (two-line and `wide`):

```tsx
{drafts && <span className="message-row-draft">Draft</span>}
```

CSS beside the other `.message-row-*` rules (role token, no literal colour):

```css
.message-row-draft {
  color: var(--danger);
  font-size: 12px;
  margin-right: 6px;
  flex: none;
}
```

`MailLayout.tsx` — `selectMessage` branches on the drafts role before writing `uid` to the URL (a draft opens as an editor, not a reading pane):

```ts
const openDraft = useOpenDraft()
const { data: identityList } = useIdentities()

function selectMessage(nextUid: number) {
  if (!folder) return
  if (folderRole === 'drafts') { void openDraftInComposer(nextUid); return }
  setResultFolder(null)
  setParams({ folder, uid: String(nextUid) })
}

async function openDraftInComposer(uid: number) {
  try {
    const opened = await openDraft.mutateAsync({ folder: folder!, uid })
    const seed = buildDraftSeed(opened, identityList ?? [], { folderPath: folder!, uid })
    navigate('/mail/compose', { state: { from: folder, seed } })
  } catch (error) {
    notify((error as Error).message || 'Could not open the draft', 'error')
  }
}
```

(Adapt names to `MailLayout`'s actual toast helper and role source — it already derives the open folder's role for `MessageList`'s `folderRole` prop; reuse that value. A cross-folder search hit into a drafts folder still opens the reader — accepted for this slice; note it in the task report.)

- [ ] **Step 4: GREEN** — focused, then full `npm test`, `npm run typecheck`, `npm run lint`, `npm run build`.

- [ ] **Step 5: Commit** — message: `Webmail 2c3a: drafts folder lists recipients and opens the composer`.

---

### Task 10: Docs + final verification

**Files:**
- Modify: `src/frontend/CLAUDE.md` (the Project paragraph still says "There are no drafts yet (that ships in slice 2c3)" — describe the shipped behavior: Save draft button, three-way leave dialog, changed-since-save dirty including the From-only edit, drafts folder click-to-compose, "To:" rows; update the `dirty` note that anticipated the change)
- Modify: `src/snoopy.microservice/CLAUDE.md` (the `MailController` endpoint list gains `POST /api/Mail/Drafts` and `POST /api/Mail/Drafts/Open` with their status codes, worded like the neighbouring entries)

- [ ] **Step 1: Update both CLAUDE.md files.** Keep each addition proportionate to the neighbouring entries — a few lines, not an essay.

- [ ] **Step 2: Full verification** — backend: `dotnet test` then `dotnet build -c Release` (zero warnings). Frontend: `npm test`, `npm run typecheck`, `npm run lint`, `npm run build`.

- [ ] **Step 3: Commit** — message: `Webmail 2c3a: document drafts in the module guides`.

---

## Self-review notes (already applied)

- Spec §4.1 (save, replace, empty draft valid, foreign From 400, 502 without drafts folder) → T4/T5; §4.2 (open = envelope + EditAsNew preparation, 404) → T5; §4.3 (send then client-side delete) → T8; §4.4 (`summary.to`) → T1/T6; §5.1 (button, dialog, changed-since-save, inline lifecycle into `useStagedAttachments`) → T8; §5.2 (click-to-compose, "To:" + Draft marker) → T9; §7 verification spread across every task's GREEN step + T10.
- Type consistency: `SaveDraftRequest : SendMessageRequest` (+`ReplaceUid`), `SavedDraft(Uid, FolderPath)`, `OpenedDraft` fields match `mailTypes.ts` shapes camel-cased; `buildDraftSeed(opened, identities, ref)` consumed by T9 exactly as produced by T7; `useStagedAttachments(initial, inlineIds)` produced in T8 and consumed there only.
- The factory constants keep Send's existing wire behavior: T2 explicitly instructs preserving the current literal values.
