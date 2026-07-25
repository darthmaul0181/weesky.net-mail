# Webmail 2c2b — Reply / Reply-all / Forward / Edit-as-new Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reply, reply-all, forward and edit-as-new from the reader, with a server-prepared quotable body, RFC 5322 threading, inline-image round-trip (`cid:` ↔ staged URL) and server-side re-staging of forwarded attachments.

**Architecture:** Hybrid split (spec `docs/superpowers/specs/2026-07-25-webmail-reply-forward-2c2b-design.md`): the backend transcribes threading headers onto the message detail, serves a lazily-prepared quotable body (`POST /api/Mail/Messages/PrepareQuote`) with inline/real parts re-staged into the existing staged-attachment store, and packs inline parts back as `multipart/related` at send. The frontend computes recipients / subject / attribution / threading in colocated pure modules and seeds the existing `ComposeView` through router state.

**Tech Stack:** ASP.NET Core (.NET 10), MimeKit/MailKit, Ganss.Xss + AngleSharp, xUnit + Moq — React 18/TS, Squire, TanStack Query v5, Vitest + RTL.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-25-webmail-reply-forward-2c2b-design.md`. Verbatim values:
  - `POST /api/Mail/Messages/PrepareQuote`, body `{ "folder": string, "uid": number, "purpose": "reply" | "forward" | "editAsNew" }`; `editAsNew` behaves exactly like `forward` server-side.
  - `GET /api/Mail/Attachments/{id:guid}/content` — owner-only, `404` for a foreign/unknown id.
  - Attribution line: `On {date}, {name} <{address}> wrote:` — forward banner: `---------- Forwarded message ----------`.
  - Subject prefixes `Re:` / `Fwd:`, non-stacking, recognising `Re:` / `Fwd:` / `Fw:` case-insensitively.
  - The outgoing sanitizer must open **both** cid gates: the Ganss scheme allowlist AND the img-source cull.
  - Reply-all preserves the original To/Cc split; reply to my own message targets the original `To`; unreferenced inline parts are never packed.
- All code, comments, UI copy and docs in **English**.
- Backend: run `dotnet test` from `src/snoopy.microservice` — **never** `--no-build` when new test files were added. `Assert.IsType<T>` checks the exact runtime type: `BadRequest(body)` ⇒ `Assert.IsType<BadRequestObjectResult>`.
- Frontend: run from `src/frontend`: `npx vitest run <path>` for one file, `npm test` for the suite, `npm run typecheck`, `npm run lint`, `npm run build`.
- Git: stage files **explicitly** (never `git add -A`). Never commit `.claude/settings.local.json`, `src/frontend/src/App.test.tsx`, `src/snoopy.microservice/ApiDocumentation.xml`. Commit messages ≤ 2 body lines, never starting or ending with `@`, passed via Bash heredoc `git commit -F -`. **Never push** — pushing deploys.
- C# style: file-scoped namespaces, one type per file, records for DTOs, collection expressions, `internal sealed` by default, structured logging via `ILogger`, cancellation tokens on async methods.

## File Structure

Backend — modified: `Models/Mail/MailMessageDetail.cs`, `Models/Mail/StagedAttachmentInfo.cs`, `Models/Mail/SendMessageRequest.cs`, `Services/ImapSession.cs`, `Services/OutgoingMailSanitizer.cs`, `Services/IStagedAttachmentStore.cs`, `Services/StagedAttachmentStore.cs`, `Services/MailSender.cs`, `Repositories/IMailMessageRepository.cs`, `Repositories/MailMessageRepository.cs`, `Controllers/MailController.cs`, `Program.cs`, `CLAUDE.md`. Created: `Models/Mail/QuotePurpose.cs`, `Models/Mail/PreparedQuote.cs`, `Models/Mail/PrepareQuoteRequest.cs`, `Services/IQuotePreparer.cs`, `Services/QuotePreparer.cs` (all backend paths relative to `src/snoopy.microservice/`).

Frontend — modified: `src/api.js`, `src/modules/mail/api/mailTypes.ts`, `src/modules/mail/queries.ts`, `src/modules/mail/compose/ComposeView.tsx`, `src/modules/mail/compose/SquireEditor.tsx`, `src/modules/mail/compose/useStagedAttachments.ts`, `src/modules/mail/reader/MessageReader.tsx`, `src/modules/mail/reader/ReaderActions.tsx`. Created: `src/modules/mail/compose/replyModel.ts`, `threadingHeaders.ts`, `quote.ts`, `composeSeed.ts` (+ one `.test.ts` each), `src/icons/ReplyIcon.tsx`, `ReplyAllIcon.tsx`, `ForwardIcon.tsx` (all frontend paths relative to `src/frontend/`).

---

### Task 1: Threading headers and Bcc on the message detail

**Files:**
- Modify: `src/snoopy.microservice/Models/Mail/MailMessageDetail.cs`
- Modify: `src/snoopy.microservice/Services/ImapSession.cs` (~line 743-764, the `GetMessageAsync` initializer)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapSessionTests.cs`

**Interfaces:**
- Consumes: `ImapSession.ToAddressInfos(InternetAddressList?)` (exists, `ImapSession.cs:716-717`).
- Produces: `MailMessageDetail.MessageId (string?)`, `.References (List<string>)`, `.InReplyTo (string?)`, `.ReplyTo (List<MailAddressInfo>)`, `.Bcc (List<MailAddressInfo>)`; `internal static void ImapSession.ApplyThreading(MailMessageDetail, MimeMessage)`.

- [ ] **Step 1: Write the failing tests**

In `ImapSessionTests.cs`, add (with `using MimeKit;` if absent):

```csharp
[Fact]
public void ApplyThreading_TranscribesTheHeaders()
{
    var message = new MimeMessage();
    message.MessageId = "current@id";
    message.InReplyTo = "parent@id";
    message.References.Add("grandparent@id");
    message.References.Add("parent@id");
    message.ReplyTo.Add(new MailboxAddress("List", "list@x.example"));
    message.Bcc.Add(new MailboxAddress("Hidden", "bcc@x.example"));

    var detail = new MailMessageDetail();
    ImapSession.ApplyThreading(detail, message);

    Assert.Equal("current@id", detail.MessageId);
    Assert.Equal("parent@id", detail.InReplyTo);
    Assert.Equal(new[] { "grandparent@id", "parent@id" }, detail.References);
    Assert.Equal("list@x.example", Assert.Single(detail.ReplyTo).Address);
    Assert.Equal("bcc@x.example", Assert.Single(detail.Bcc).Address);
}

[Fact]
public void ApplyThreading_DefaultsToNullAndEmptyWhenAbsent()
{
    // MimeMessage's constructor generates a Message-Id — remove it to model a header-less original.
    var message = new MimeMessage();
    message.Headers.Remove(HeaderId.MessageId);

    var detail = new MailMessageDetail();
    ImapSession.ApplyThreading(detail, message);

    Assert.Null(detail.MessageId);
    Assert.Null(detail.InReplyTo);
    Assert.Empty(detail.References);
    Assert.Empty(detail.ReplyTo);
    Assert.Empty(detail.Bcc);
}
```

- [ ] **Step 2: Run and verify both fail**

Run: `dotnet test --filter ApplyThreading` (from `src/snoopy.microservice`)
Expected: compilation failure — `ApplyThreading` and the new detail members do not exist.

- [ ] **Step 3: Implement**

In `MailMessageDetail.cs`, after the `Cc` property:

```csharp
    /// <summary>RFC 5322 Message-Id, bare (no angle brackets). Null when the message carries none.</summary>
    public string? MessageId { get; set; }

    /// <summary>References chain, oldest first, bare ids. Empty when absent.</summary>
    public List<string> References { get; set; } = [];

    /// <summary>In-Reply-To, bare id. Null when absent.</summary>
    public string? InReplyTo { get; set; }

    /// <summary>Reply-To mailboxes — the reply target when present. Empty when absent.</summary>
    public List<MailAddressInfo> ReplyTo { get; set; } = [];

    /// <summary>Bcc mailboxes — kept on a Sent copy; empty on received mail. Feeds Edit-as-new.</summary>
    public List<MailAddressInfo> Bcc { get; set; } = [];
```

In `ImapSession.cs`, next to `ToAddressInfos`:

```csharp
    /// <summary>Threading and reply-routing headers — 2c2b's transcription duty on the detail.</summary>
    internal static void ApplyThreading(MailMessageDetail detail, MimeMessage message)
    {
        detail.MessageId = string.IsNullOrWhiteSpace(message.MessageId) ? null : message.MessageId;
        detail.References = message.References?.ToList() ?? [];
        detail.InReplyTo = string.IsNullOrWhiteSpace(message.InReplyTo) ? null : message.InReplyTo;
        detail.ReplyTo = ToAddressInfos(message.ReplyTo);
        detail.Bcc = ToAddressInfos(message.Bcc);
    }
```

In `GetMessageAsync`, right after the `var detail = new MailMessageDetail { … };` initializer closes (before the attachments loop), add:

```csharp
            ApplyThreading(detail, message);
```

- [ ] **Step 4: Run and verify green**

Run: `dotnet test` (new test methods in an existing file — a full `dotnet test` is still required by the repo rule whenever in doubt; here the file existed, `--no-build` is still forbidden because Step 3 changed sources)
Expected: PASS, full suite green.

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Models/Mail/MailMessageDetail.cs src/snoopy.microservice/Services/ImapSession.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapSessionTests.cs
git commit -F - <<'EOF'
Webmail 2c2b: transcribe threading headers and Bcc on the detail
EOF
```

---

### Task 2: The outgoing sanitizer opens both cid gates

**Files:**
- Modify: `src/snoopy.microservice/Services/OutgoingMailSanitizer.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/OutgoingMailSanitizerTests.cs`

**Interfaces:**
- Produces: `Prepare(html)` now keeps `<img src="cid:…">` with its `src` intact. No signature change.

**Context — the two gates (spec § 3.6):** `cid:` is killed twice today: Ganss's scheme allowlist (`OutgoingMailSanitizer.cs:25-28`) strips the `src` *attribute* (unknown scheme) before the image loop runs, then `IsRemote` (lines 40-44, 53-55) removes the whole `<img>`. Fixing only one gate silently loses the images.

- [ ] **Step 1: Write the failing tests**

In `OutgoingMailSanitizerTests.cs` (follow the file's existing construction pattern for the sanitizer instance):

```csharp
[Fact]
public void Prepare_KeepsACidImageWithItsSrcIntact()
{
    var body = _sanitizer.Prepare("<p>Hi</p><img src=\"cid:logo@mail\">");

    Assert.Contains("cid:logo@mail", body.Html);
    Assert.Contains("<img", body.Html);
}

[Fact]
public void Prepare_StillRemovesNonRemoteNonCidImages()
{
    var body = _sanitizer.Prepare(
        "<img src=\"file:///etc/passwd\"><img src=\"/relative.png\"><img src=\"data:image/png;base64,AA==\">");

    Assert.DoesNotContain("<img", body.Html);
}
```

- [ ] **Step 2: Run and verify the first fails**

Run: `dotnet test --filter Prepare_KeepsACidImage`
Expected: FAIL — the cid image is removed today. (The second test should already pass; keep it as the boundary lock.)

- [ ] **Step 3: Implement**

In the constructor, after `AllowedSchemes.Add("mailto");`:

```csharp
        // cid: references an embedded part — no file access, no tracker. Without this, Ganss
        // strips the src attribute before the image cull below ever sees it (the first gate).
        _sanitizer.AllowedSchemes.Add("cid");
```

Rename `IsRemote` to `IsAllowedImageSource` (update the call site and its comment):

```csharp
    // Ganss has already dropped every scheme but http, https, mailto and cid, so what is left to
    // reject is the schemeless src: relative to nothing once the message leaves us. cid is the
    // second gate — an inline part reference is a legitimate outgoing source since 2c2b.
    private static bool IsAllowedImageSource(string src) =>
        src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase);
```

Also update the "No cid: machinery in 2c1" comment above the image loop — it is no longer true; replace with: `// An image with no usable source is noise in the wire format; cid: is usable since 2c2b.`

- [ ] **Step 4: Run and verify green**

Run: `dotnet test`
Expected: PASS, full suite green (existing sanitizer tests must not regress).

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Services/OutgoingMailSanitizer.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/OutgoingMailSanitizerTests.cs
git commit -F - <<'EOF'
Webmail 2c2b: the outgoing sanitizer keeps cid: images

Both gates open: the Ganss scheme allowlist and the img-source cull.
EOF
```

---

### Task 3: ContentId on the staged store + the content endpoint

**Files:**
- Modify: `src/snoopy.microservice/Models/Mail/StagedAttachmentInfo.cs`
- Modify: `src/snoopy.microservice/Services/IStagedAttachmentStore.cs`
- Modify: `src/snoopy.microservice/Services/StagedAttachmentStore.cs`
- Modify: `src/snoopy.microservice/Controllers/MailController.cs` (after `DeleteAttachment`, ~line 704)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/StagedAttachmentStoreTests.cs`, `…/Controllers/MailControllerTests.cs`

**Interfaces:**
- Produces: `record StagedAttachmentInfo(Guid Id, string FileName, long Size, string ContentType, string? ContentId = null)`; `SaveAsync(accountId, fileName, contentType, content, cancellationToken, string? contentId = null)`; `GET /api/Mail/Attachments/{id:guid}/content` → 200 file / 404.
- Consumed by: Task 4 (staging with contentId), Task 6 (inline split at send), frontend `stagedAttachmentUrl`.

- [ ] **Step 1: Write the failing tests**

`StagedAttachmentStoreTests.cs` (follow the file's existing temp-root store construction):

```csharp
[Fact]
public async Task SaveAsync_CarriesTheContentIdThrough()
{
    var store = CreateStore(); // the file's existing factory
    using var content = new MemoryStream(new byte[] { 1, 2, 3 });

    var result = await store.SaveAsync("acc", "logo.png", "image/png", content, CancellationToken.None, "logo@mail");

    Assert.True(result.IsSuccess);
    Assert.Equal("logo@mail", result.Value.ContentId);
}

[Fact]
public async Task SaveAsync_DefaultsContentIdToNull()
{
    var store = CreateStore();
    using var content = new MemoryStream(new byte[] { 1 });

    var result = await store.SaveAsync("acc", "a.pdf", "application/pdf", content, CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Null(result.Value.ContentId);
}
```

`MailControllerTests.cs` — follow the file's existing controller construction (mocked `IStagedAttachmentStore` already exists for the upload tests):

```csharp
[Fact]
public void GetStagedAttachment_ServesTheOwnersFile()
{
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
    try
    {
        var id = Guid.NewGuid();
        var info = new StagedAttachmentInfo(id, "logo.png", 3, "image/png", "logo@mail");
        _staged.Setup(s => s.Open(It.IsAny<string>(), id))
            .Returns(Result.Success(new StagedAttachment(info, path)));

        var result = _controller.GetStagedAttachment(id);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal("nosniff", _controller.Response.Headers.XContentTypeOptions);
    }
    finally { File.Delete(path); }
}

[Fact]
public void GetStagedAttachment_AnswersNotFoundForAForeignId()
{
    _staged.Setup(s => s.Open(It.IsAny<string>(), It.IsAny<Guid>()))
        .Returns(Result.Failure<StagedAttachment>("unknown_attachment"));

    var result = _controller.GetStagedAttachment(Guid.NewGuid());

    Assert.IsType<NotFoundObjectResult>(result);
}
```

(Adapt `_staged` / `_controller` to the fixture's actual field names.)

- [ ] **Step 2: Run and verify they fail**

Run: `dotnet test --filter "SaveAsync_Carries|GetStagedAttachment"`
Expected: compilation failure — `ContentId`, the `contentId` parameter and `GetStagedAttachment` do not exist.

- [ ] **Step 3: Implement**

`StagedAttachmentInfo.cs`:

```csharp
/// <summary>What the upload endpoint answers and the compose client holds on to.
/// A non-null ContentId marks an inline body resource (cid part) to pack as multipart/related.</summary>
public sealed record StagedAttachmentInfo(Guid Id, string FileName, long Size, string ContentType, string? ContentId = null);
```

`IStagedAttachmentStore.cs` — extend `SaveAsync` (the trailing optional keeps every existing call site intact):

```csharp
    /// <summary>Streams one upload to disk. Fails when the file or the account total exceeds the caps.
    /// A contentId marks the file as an inline body resource rather than a plain attachment.</summary>
    Task<Result<StagedAttachmentInfo>> SaveAsync(string accountId, string fileName, string contentType, Stream content, CancellationToken cancellationToken, string? contentId = null);
```

`StagedAttachmentStore.cs` — same signature on the implementation; thread it into the info (line ~74):

```csharp
            var info = new StagedAttachmentInfo(id, Path.GetFileName(fileName), written,
                string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType, contentId);
```

`MailController.cs`, after `DeleteAttachment`:

```csharp
    /// <summary>
    /// Serves one staged attachment back to its owner, so the composer can display the inline
    /// images PrepareQuote staged. Always an attachment disposition plus nosniff: an img
    /// subresource renders regardless, while navigating to the URL downloads instead of
    /// rendering staged mail content on our origin.
    /// </summary>
    /// <param name="id">staged attachment id</param>
    /// <response code="200">The staged bytes</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">Unknown id, or one staged by another account</response>
    [HttpGet("Attachments/{id:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetStagedAttachment(Guid id)
    {
        var result = _staged.Open(AuthenticatedUser.WebmailUid.ToString(), id);
        if (result.IsFailure) return NotFound(ResultEnveloppe.CreateErrorEnveloppe("Attachment not found"));

        Response.Headers.XContentTypeOptions = "nosniff";
        try
        {
            var stream = System.IO.File.OpenRead(result.Value.FilePath);
            return File(stream, result.Value.Info.ContentType, result.Value.Info.FileName);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Vanished between Open and read (TTL sweep / concurrent DELETE).
            return NotFound(ResultEnveloppe.CreateErrorEnveloppe("Attachment not found"));
        }
    }
```

- [ ] **Step 4: Run and verify green**

Run: `dotnet test`
Expected: PASS, full suite green.

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Models/Mail/StagedAttachmentInfo.cs src/snoopy.microservice/Services/IStagedAttachmentStore.cs src/snoopy.microservice/Services/StagedAttachmentStore.cs src/snoopy.microservice/Controllers/MailController.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/StagedAttachmentStoreTests.cs src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs
git commit -F - <<'EOF'
Webmail 2c2b: ContentId on staged attachments + owner content endpoint
EOF
```

---

### Task 4: QuotePreparer — the quotable body and the re-staging

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/QuotePurpose.cs`, `src/snoopy.microservice/Models/Mail/PreparedQuote.cs`, `src/snoopy.microservice/Services/IQuotePreparer.cs`, `src/snoopy.microservice/Services/QuotePreparer.cs`
- Modify: `src/snoopy.microservice/Program.cs` (DI registration)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/QuotePreparerTests.cs` (new file — remember: `dotnet test`, never `--no-build`)

**Interfaces:**
- Consumes: `IOutgoingMailSanitizer.Prepare` (Task 2's cid-keeping version), `IStagedAttachmentStore.SaveAsync(…, contentId)` (Task 3).
- Produces: `enum QuotePurpose { Reply, Forward, EditAsNew }`; `record PreparedQuote(string QuotableHtml, IReadOnlyList<StagedAttachmentInfo> Attachments)`; `IQuotePreparer.PrepareAsync(string accountId, MimeMessage message, QuotePurpose purpose, CancellationToken ct) → Result<PreparedQuote>`. Consumed by Task 5's endpoint.

- [ ] **Step 1: Write the failing tests**

`QuotePreparerTests.cs`. Build the store the way `StagedAttachmentStoreTests` does (temp root + `IOptionsMonitor<MailOptions>` mock + `TimeProvider.System`); use the real `OutgoingMailSanitizer`.

```csharp
using CSharpFunctionalExtensions;
using MimeKit;
using MimeKit.Utils;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;

public sealed class QuotePreparerTests : IDisposable
{
    // Copy the temp-root + options fixture shape from StagedAttachmentStoreTests.
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly StagedAttachmentStore _store;
    private readonly QuotePreparer _preparer;

    public QuotePreparerTests()
    {
        _store = CreateStore(maxMessageSizeMb: 25);
        _preparer = new QuotePreparer(new OutgoingMailSanitizer(), _store);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static MimeMessage MessageWithInlineImageAndPdf()
    {
        var builder = new BodyBuilder
        {
            HtmlBody = "<p>Hello</p><img src=\"cid:logo@mail\"><script>alert(1)</script>",
        };
        var image = builder.LinkedResources.Add("logo.png", new byte[] { 1, 2, 3 }, new ContentType("image", "png"));
        image.ContentId = "logo@mail";
        builder.Attachments.Add("report.pdf", new byte[] { 9, 9 }, new ContentType("application", "pdf"));
        return new MimeMessage { Body = builder.ToMessageBody() };
    }

    [Fact]
    public async Task Reply_StagesInlineImagesAndRewritesTheirSrc_ButNoAttachments()
    {
        var result = await _preparer.PrepareAsync("acc", MessageWithInlineImageAndPdf(), QuotePurpose.Reply, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var staged = Assert.Single(result.Value.Attachments);
        Assert.Equal("logo@mail", staged.ContentId);
        Assert.Contains($"/api/Mail/Attachments/{staged.Id}/content", result.Value.QuotableHtml);
        Assert.DoesNotContain("cid:", result.Value.QuotableHtml);
        Assert.DoesNotContain("script", result.Value.QuotableHtml);
    }

    [Fact]
    public async Task Forward_AlsoRestagesTheRealAttachments()
    {
        var result = await _preparer.PrepareAsync("acc", MessageWithInlineImageAndPdf(), QuotePurpose.Forward, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Attachments.Count);
        Assert.Contains(result.Value.Attachments, a => a.ContentId == "logo@mail");
        Assert.Contains(result.Value.Attachments, a => a.ContentId == null && a.FileName == "report.pdf");
    }

    [Fact]
    public async Task EditAsNew_BehavesExactlyLikeForward()
    {
        var result = await _preparer.PrepareAsync("acc", MessageWithInlineImageAndPdf(), QuotePurpose.EditAsNew, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Attachments.Count);
    }

    [Fact]
    public async Task TextOnlyOriginal_IsEscapedWithLineBreaks()
    {
        var message = new MimeMessage { Body = new TextPart("plain") { Text = "a < b\nsecond line" } };

        var result = await _preparer.PrepareAsync("acc", message, QuotePurpose.Reply, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("a &lt; b<br>second line", result.Value.QuotableHtml);
        Assert.Empty(result.Value.Attachments);
    }

    [Fact]
    public async Task ACidWithNoMatchingImagePart_LosesItsImg()
    {
        var builder = new BodyBuilder { HtmlBody = "<p>Hi</p><img src=\"cid:gone@mail\">" };
        var message = new MimeMessage { Body = builder.ToMessageBody() };

        var result = await _preparer.PrepareAsync("acc", message, QuotePurpose.Reply, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("cid:", result.Value.QuotableHtml);
        Assert.DoesNotContain("<img", result.Value.QuotableHtml);
        Assert.Empty(result.Value.Attachments);
    }

    [Fact]
    public async Task AStagingFailure_FailsTheWholePreparation()
    {
        // A 0 MB cap makes every SaveAsync fail — the store's own refusal must surface.
        var preparer = new QuotePreparer(new OutgoingMailSanitizer(), CreateStore(maxMessageSizeMb: 0));

        var result = await preparer.PrepareAsync("acc", MessageWithInlineImageAndPdf(), QuotePurpose.Reply, CancellationToken.None);

        Assert.True(result.IsFailure);
    }
}
```

- [ ] **Step 2: Run and verify they fail**

Run: `dotnet test` (new file)
Expected: compilation failure — `QuotePurpose`, `PreparedQuote`, `QuotePreparer` do not exist.

- [ ] **Step 3: Implement**

`Models/Mail/QuotePurpose.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>What the composer opens the prepared quote as. EditAsNew stages like Forward.</summary>
public enum QuotePurpose { Reply, Forward, EditAsNew }
```

`Models/Mail/PreparedQuote.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>The quotable body (outbound-sanitised, cid images rewritten to staged URLs) and the staged parts.</summary>
public sealed record PreparedQuote(string QuotableHtml, IReadOnlyList<StagedAttachmentInfo> Attachments);
```

`Services/IQuotePreparer.cs`:

```csharp
using CSharpFunctionalExtensions;
using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>Builds the quotable body of an original and stages the parts a reply/forward carries over.</summary>
public interface IQuotePreparer
{
    Task<Result<PreparedQuote>> PrepareAsync(string accountId, MimeMessage message, QuotePurpose purpose, CancellationToken cancellationToken);
}
```

`Services/QuotePreparer.cs`:

```csharp
using AngleSharp.Html.Parser;
using CSharpFunctionalExtensions;
using Ganss.Xss;
using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// One pass over the original: the raw body goes through the OUTGOING policy (it is about to be
/// sent again, not displayed), then every cid the body references is staged as an inline part and
/// its src rewritten to the staged-content URL. The invariant is a quotableHtml with no cid: left.
/// Forward and EditAsNew re-stage the real attachments too — server-side, never via the browser.
/// </summary>
internal sealed class QuotePreparer : IQuotePreparer
{
    private readonly IOutgoingMailSanitizer _sanitizer;
    private readonly IStagedAttachmentStore _staged;
    private readonly HtmlParser _parser = new();

    public QuotePreparer(IOutgoingMailSanitizer sanitizer, IStagedAttachmentStore staged)
    {
        _sanitizer = sanitizer;
        _staged = staged;
    }

    public async Task<Result<PreparedQuote>> PrepareAsync(
        string accountId, MimeMessage message, QuotePurpose purpose, CancellationToken cancellationToken)
    {
        var attachments = new List<StagedAttachmentInfo>();
        string quotable;

        var raw = message.HtmlBody;
        if (string.IsNullOrEmpty(raw))
        {
            // A text-only original is quoted from its TextBody — the composer knows one input format.
            quotable = TextToHtml(message.TextBody ?? string.Empty);
        }
        else
        {
            var sanitized = _sanitizer.Prepare(raw).Html;
            var document = _parser.ParseDocument($"<body>{sanitized}</body>");
            var stagedByCid = new Dictionary<string, StagedAttachmentInfo>(StringComparer.Ordinal);

            foreach (var img in document.Body!.QuerySelectorAll("img").ToList())
            {
                var src = img.GetAttribute("src") ?? string.Empty;
                if (!src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)) continue;

                var contentId = src[4..];
                if (!stagedByCid.TryGetValue(contentId, out var info))
                {
                    var part = FindImagePart(message, contentId);
                    if (part == null) { img.Remove(); continue; } // dangling or non-image cid
                    var staged = await StagePartAsync(accountId, part, contentId, cancellationToken);
                    if (staged.IsFailure) return Result.Failure<PreparedQuote>(staged.Error);
                    info = staged.Value;
                    stagedByCid[contentId] = info;
                    attachments.Add(info);
                }
                img.SetAttribute("src", $"/api/Mail/Attachments/{info.Id}/content");
            }

            // Same re-serialisation as the sanitizer: Ganss's formatter keeps attribute escaping.
            quotable = document.Body.ChildNodes.ToHtml(HtmlFormatter.Instance);
        }

        if (purpose is QuotePurpose.Forward or QuotePurpose.EditAsNew)
        {
            foreach (var entity in message.Attachments)
            {
                var staged = await StageAttachmentAsync(accountId, entity, cancellationToken);
                if (staged.IsFailure) return Result.Failure<PreparedQuote>(staged.Error);
                attachments.Add(staged.Value);
            }
        }

        // On a mid-way failure above, already-staged files linger — the TTL sweep reclaims them.
        return Result.Success(new PreparedQuote(quotable, attachments));
    }

    private static MimePart? FindImagePart(MimeMessage message, string contentId) =>
        message.BodyParts.OfType<MimePart>().FirstOrDefault(p =>
            string.Equals(p.ContentId, contentId, StringComparison.Ordinal)
            && p.ContentType.IsMimeType("image", "*"));

    private async Task<Result<StagedAttachmentInfo>> StagePartAsync(
        string accountId, MimePart part, string? contentId, CancellationToken cancellationToken)
    {
        // Content.Open() decodes on the fly — no in-memory buffering of a possibly large part.
        await using var content = part.Content.Open();
        return await _staged.SaveAsync(
            accountId, part.FileName ?? "inline", part.ContentType.MimeType, content, cancellationToken, contentId);
    }

    private async Task<Result<StagedAttachmentInfo>> StageAttachmentAsync(
        string accountId, MimeEntity entity, CancellationToken cancellationToken)
    {
        if (entity is MimePart part) return await StagePartAsync(accountId, part, null, cancellationToken);

        // An attached message (message/rfc822) has no decodable content; its wire form is the file.
        await using var buffer = new MemoryStream();
        await entity.WriteToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        var name = entity.ContentDisposition?.FileName ?? "attached-message.eml";
        return await _staged.SaveAsync(accountId, name, "message/rfc822", buffer, cancellationToken);
    }

    /// <summary>Escaped text with its line structure rendered — the text-only quoting path.</summary>
    internal static string TextToHtml(string text)
    {
        var escaped = System.Net.WebUtility.HtmlEncode(text);
        return $"<div>{escaped.Replace("\r\n", "\n").Replace("\n", "<br>")}</div>";
    }
}
```

`Program.cs`: locate the existing registration (`grep -n "IOutgoingMailSanitizer" src/snoopy.microservice/Program.cs`) and add beside it:

```csharp
builder.Services.AddSingleton<IQuotePreparer, QuotePreparer>();
```

- [ ] **Step 4: Run and verify green**

Run: `dotnet test`
Expected: PASS, full suite green. Note: `MessageWithInlineImageAndPdf` relies on Task 2 — the cid image must survive the outbound policy for the rewrite to find it.

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Models/Mail/QuotePurpose.cs src/snoopy.microservice/Models/Mail/PreparedQuote.cs src/snoopy.microservice/Services/IQuotePreparer.cs src/snoopy.microservice/Services/QuotePreparer.cs src/snoopy.microservice/Program.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/QuotePreparerTests.cs
git commit -F - <<'EOF'
Webmail 2c2b: QuotePreparer

Outbound-sanitised quotable body, inline parts staged with their cid,
forward/editAsNew re-stage the real attachments server-side.
EOF
```

---

### Task 5: Raw message access + the PrepareQuote endpoint

**Files:**
- Modify: `src/snoopy.microservice/Services/ImapSession.cs` (new method beside `GetMessageAsync`)
- Modify: `src/snoopy.microservice/Repositories/IMailMessageRepository.cs`, `src/snoopy.microservice/Repositories/MailMessageRepository.cs`
- Create: `src/snoopy.microservice/Models/Mail/PrepareQuoteRequest.cs`
- Modify: `src/snoopy.microservice/Controllers/MailController.cs` (endpoint + `IQuotePreparer` constructor parameter), `src/snoopy.microservice/CLAUDE.md` (MailController endpoint list)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs`

**Interfaces:**
- Consumes: `IQuotePreparer` (Task 4), `ImapSession.MessageNotFound` (exists), `_credentials.Retrieve(Request)` (existing pattern).
- Produces: `IMailMessageRepository.GetMimeMessageAsync(User, string password, string folderPath, uint uid, CancellationToken) → Result<MimeMessage>`; `POST /api/Mail/Messages/PrepareQuote` → 200 `PreparedQuote` / 400 / 401 / 404 / 502.

- [ ] **Step 1: Write the failing tests**

In `MailControllerTests.cs` — the fixture gains a `Mock<IQuotePreparer> _quotes` and a `Mock<IMailMessageRepository>` already exists for the message tests; pass `_quotes.Object` to the controller construction (all existing construction sites must be updated):

```csharp
[Fact]
public async Task PrepareQuote_RefusesAnUnknownPurpose()
{
    var result = await _controller.PrepareQuote(
        new PrepareQuoteRequest { Folder = "INBOX", Uid = 1, Purpose = "resend" }, CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
}

[Fact]
public async Task PrepareQuote_RefusesAMissingFolder()
{
    var result = await _controller.PrepareQuote(
        new PrepareQuoteRequest { Folder = " ", Uid = 1, Purpose = "reply" }, CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
}

[Fact]
public async Task PrepareQuote_MapsMessageNotFoundTo404()
{
    // Arrange credentials success the way the fixture's other message tests do.
    _messages.Setup(m => m.GetMimeMessageAsync(It.IsAny<User>(), It.IsAny<string>(), "INBOX", 7u, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Failure<MimeMessage>(ImapSession.MessageNotFound));

    var result = await _controller.PrepareQuote(
        new PrepareQuoteRequest { Folder = "INBOX", Uid = 7, Purpose = "reply" }, CancellationToken.None);

    Assert.IsType<NotFoundObjectResult>(result.Result);
}

[Fact]
public async Task PrepareQuote_AnswersThePreparedQuote()
{
    _messages.Setup(m => m.GetMimeMessageAsync(It.IsAny<User>(), It.IsAny<string>(), "INBOX", 7u, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Success(new MimeMessage()));
    var prepared = new PreparedQuote("<p>q</p>", []);
    _quotes.Setup(q => q.PrepareAsync(It.IsAny<string>(), It.IsAny<MimeMessage>(), QuotePurpose.Forward, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Success(prepared));

    var result = await _controller.PrepareQuote(
        new PrepareQuoteRequest { Folder = "INBOX", Uid = 7, Purpose = "forward" }, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    Assert.Same(prepared, ok.Value);
}

[Fact]
public async Task PrepareQuote_MapsAStagingRefusalTo400()
{
    _messages.Setup(m => m.GetMimeMessageAsync(It.IsAny<User>(), It.IsAny<string>(), "INBOX", 7u, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Success(new MimeMessage()));
    _quotes.Setup(q => q.PrepareAsync(It.IsAny<string>(), It.IsAny<MimeMessage>(), It.IsAny<QuotePurpose>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Failure<PreparedQuote>("The attachment exceeds the 25 MB limit"));

    var result = await _controller.PrepareQuote(
        new PrepareQuoteRequest { Folder = "INBOX", Uid = 7, Purpose = "forward" }, CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
}
```

Also add the 401 case following the file's existing credentials-failure test pattern, and a 502 case (`GetMimeMessageAsync` failing with any other error → `ObjectResult` with `StatusCode == 502`).

- [ ] **Step 2: Run and verify they fail**

Run: `dotnet test`
Expected: compilation failure — `PrepareQuoteRequest`, `GetMimeMessageAsync`, `PrepareQuote` do not exist.

- [ ] **Step 3: Implement**

`ImapSession.cs`, after `GetMessageAsync` (add `using MailKit;` if not present — `MessageNotFoundException` lives there):

```csharp
    /// <summary>The message as MimeKit parsed it — PrepareQuote needs the raw body and its parts.</summary>
    public async Task<Result<MimeMessage>> GetMimeMessageAsync(string folderPath, uint uid, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        try
        {
            var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
            var message = await folder.GetMessageAsync(new UniqueId(folder.UidValidity, uid), cancellationToken);
            return Result.Success(message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MessageNotFoundException)
        {
            return Result.Failure<MimeMessage>(MessageNotFound);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read raw message {Uid} in {Folder}", uid, folderPath);
            return Result.Failure<MimeMessage>("Unable to read the message");
        }
    }
```

`IMailMessageRepository.cs`:

```csharp
    /// <summary>The raw MimeKit message, for quoting: unsanitised body, cid parts, attachments.</summary>
    Task<Result<MimeMessage>> GetMimeMessageAsync(User user, string password, string folderPath, uint uid, CancellationToken cancellationToken);
```

`MailMessageRepository.cs` (same open-session shape as `GetAsync`):

```csharp
    public async Task<Result<MimeMessage>> GetMimeMessageAsync(User user, string password, string folderPath, uint uid, CancellationToken cancellationToken)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));

        var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
        if (sessionResult.IsFailure) return Result.Failure<MimeMessage>(sessionResult.Error);
        await using var session = sessionResult.Value;

        return await session.GetMimeMessageAsync(folderPath, uid, cancellationToken);
    }
```

`Models/Mail/PrepareQuoteRequest.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>What PrepareQuote acts on: the message, and the intent the composer opens with.</summary>
public sealed record PrepareQuoteRequest
{
    public string Folder { get; init; } = string.Empty;
    public uint Uid { get; init; }

    /// <summary>"reply", "forward" or "editAsNew".</summary>
    public string Purpose { get; init; } = string.Empty;
}
```

`MailController.cs` — add `IQuotePreparer quotes` to the constructor (field `_quotes`), and after `SendMessage`:

```csharp
    /// <summary>
    /// Prepares quoting a message for the composer: the body re-sanitised by the outgoing policy
    /// with cid images rewritten to staged-content URLs, inline parts staged, and — for forward
    /// and editAsNew — the real attachments re-staged server-side. Called on the Reply / Forward
    /// / Edit-as-new click, never on ordinary reading.
    /// </summary>
    /// <param name="request">folder, uid, and the purpose ("reply", "forward" or "editAsNew")</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The quotable body and the staged parts</response>
    /// <response code="400">Missing folder, unknown purpose, or staging over the account caps</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No message with that UID in that folder</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpPost("Messages/PrepareQuote")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PreparedQuote>> PrepareQuote(PrepareQuoteRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Folder))
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));

        QuotePurpose? purpose = request.Purpose switch
        {
            "reply" => QuotePurpose.Reply,
            "forward" => QuotePurpose.Forward,
            "editAsNew" => QuotePurpose.EditAsNew,
            _ => null,
        };
        if (purpose == null)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Purpose must be reply, forward or editAsNew"));

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var message = await _messages.GetMimeMessageAsync(
            AuthenticatedUser, password.Value, request.Folder, request.Uid, cancellationToken);
        if (message.IsFailure && message.Error == ImapSession.MessageNotFound)
            return NotFound(ResultEnveloppe.CreateErrorEnveloppe(message.Error));
        if (message.IsFailure)
            return StatusCode(StatusCodes.Status502BadGateway, ResultEnveloppe.CreateErrorEnveloppe(message.Error));

        var prepared = await _quotes.PrepareAsync(
            AuthenticatedUser.WebmailUid.ToString(), message.Value, purpose.Value, cancellationToken);

        // A failure here is the staging caps talking (file size / account quota): 400, actionable.
        return FromResult(prepared);
    }
```

`CLAUDE.md` (microservice): in the `MailController` bullet, after the `POST /api/Mail/Send` description, insert: `` `POST /api/Mail/Messages/PrepareQuote` (body `{ folder, uid, purpose: "reply"|"forward"|"editAsNew" }`; outbound-sanitised quotable body with cid images rewritten to staged URLs, inline parts staged, forward/editAsNew re-stage the real attachments server-side; 200/400/401/404/502), `GET /api/Mail/Attachments/{id}/content` (serves a staged file to its owner for the composer's inline images, attachment disposition + nosniff; 200/401/404) ``.

- [ ] **Step 4: Run and verify green**

Run: `dotnet test`
Expected: PASS, full suite green.

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Services/ImapSession.cs src/snoopy.microservice/Repositories/IMailMessageRepository.cs src/snoopy.microservice/Repositories/MailMessageRepository.cs src/snoopy.microservice/Models/Mail/PrepareQuoteRequest.cs src/snoopy.microservice/Controllers/MailController.cs src/snoopy.microservice/CLAUDE.md src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs
git commit -F - <<'EOF'
Webmail 2c2b: PrepareQuote endpoint over raw message access
EOF
```

---

### Task 6: MailSender — threading headers and multipart/related

**Files:**
- Modify: `src/snoopy.microservice/Models/Mail/SendMessageRequest.cs`
- Modify: `src/snoopy.microservice/Controllers/MailController.cs` (the `SendMessage` null-normalisation, line ~732)
- Modify: `src/snoopy.microservice/Services/MailSender.cs` (`BuildMessageAsync`)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/MailSenderTests.cs`

**Interfaces:**
- Consumes: `StagedAttachmentInfo.ContentId` (Task 3), `IOutgoingMailSanitizer` keeping cid (Task 2).
- Produces: `SendMessageRequest.InReplyTo (string?)`, `.References (IReadOnlyList<string>)`. The sent `MimeMessage` carries `Message-Id` (server-generated, from-domain), `In-Reply-To`, `References`, and inline staged parts as `multipart/related` resources.

- [ ] **Step 1: Write the failing tests**

Extend `MailSenderTests.cs`, following its `CreateSender()` / `Request()` / smtp-capture pattern (the smtp mock's `SendAsync` callback captures the built `MimeMessage`):

```csharp
[Fact]
public async Task SendAsync_SetsTheThreadingHeaders()
{
    MimeMessage? sent = null;
    var sender = CreateSender();
    _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
        .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
        .ReturnsAsync(Result.Success());

    var request = Request() with { InReplyTo = "parent@id", References = ["root@id", "parent@id"] };
    var result = await sender.SendAsync(_user, "pw", request, CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Equal("parent@id", sent!.InReplyTo);
    Assert.Equal(new[] { "root@id", "parent@id" }, sent.References);
    Assert.False(string.IsNullOrEmpty(sent.MessageId));
}

[Fact]
public async Task SendAsync_WithoutThreading_SendsAFreshMessage()
{
    MimeMessage? sent = null;
    var sender = CreateSender();
    _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
        .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
        .ReturnsAsync(Result.Success());

    var result = await sender.SendAsync(_user, "pw", Request(), CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Null(sent!.InReplyTo);
    Assert.Empty(sent.References);
    Assert.False(string.IsNullOrEmpty(sent.MessageId));
}

[Fact]
public async Task SendAsync_PacksAReferencedInlinePartAsALinkedResource()
{
    var id = Guid.NewGuid();
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
    try
    {
        MimeMessage? sent = null;
        var sender = CreateSender();
        var info = new StagedAttachmentInfo(id, "logo.png", 3, "image/png", "logo@mail");
        _staged.Setup(s => s.Open(It.IsAny<string>(), id))
            .Returns(Result.Success(new StagedAttachment(info, path)));
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var request = Request() with
        {
            HtmlBody = $"<p>Hi</p><img src=\"/api/Mail/Attachments/{id}/content\">",
            AttachmentIds = [id],
        };
        var result = await sender.SendAsync(_user, "pw", request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var resource = sent!.BodyParts.OfType<MimePart>().Single(p => p.ContentId == "logo@mail");
        Assert.Equal("image/png", resource.ContentType.MimeType);
        Assert.Contains("cid:logo@mail", sent.HtmlBody);
        Assert.DoesNotContain("/api/Mail/Attachments/", sent.HtmlBody);
        // Packed as a related resource, not an attachment.
        Assert.DoesNotContain(sent.Attachments, a => a is MimePart mp && mp.ContentId == "logo@mail");
    }
    finally { File.Delete(path); }
}

[Fact]
public async Task SendAsync_SkipsAnInlinePartTheBodyNoLongerReferences()
{
    var id = Guid.NewGuid();
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    await File.WriteAllBytesAsync(path, new byte[] { 1 });
    try
    {
        MimeMessage? sent = null;
        var sender = CreateSender();
        var info = new StagedAttachmentInfo(id, "logo.png", 1, "image/png", "logo@mail");
        _staged.Setup(s => s.Open(It.IsAny<string>(), id))
            .Returns(Result.Success(new StagedAttachment(info, path)));
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        // The user deleted the image in the editor: the id is still staged, the body has no URL.
        var request = Request() with { HtmlBody = "<p>no image left</p>", AttachmentIds = [id] };
        var result = await sender.SendAsync(_user, "pw", request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(sent!.BodyParts.OfType<MimePart>(), p => p.ContentId == "logo@mail");
        _staged.Verify(s => s.Delete(It.IsAny<string>(), id), Times.Once); // still purged after send
    }
    finally { File.Delete(path); }
}
```

- [ ] **Step 2: Run and verify they fail**

Run: `dotnet test`
Expected: compilation failure — `InReplyTo` / `References` do not exist on the request.

- [ ] **Step 3: Implement**

`SendMessageRequest.cs` — replace the class doc ("Threading … comes with 2c2b") with `/// <summary>A composed message.</summary>` and add after `AttachmentIds`:

```csharp
    /// <summary>Message-Id being replied to, bare (no angle brackets). Absent on a fresh message.</summary>
    public string? InReplyTo { get; init; }

    /// <summary>References chain for the reply, oldest first, bare ids. Empty on a fresh message.</summary>
    public IReadOnlyList<string> References { get; init; } = [];
```

`MailController.SendMessage` — extend the null-normalisation:

```csharp
        request = request with { To = request.To ?? [], Cc = request.Cc ?? [], Bcc = request.Bcc ?? [], References = request.References ?? [] };
```

`MailSender.BuildMessageAsync` — replace the body-preparation and attachment block:

```csharp
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
            html = html.Replace(url, $"cid:{attachment.Info.ContentId}", StringComparison.OrdinalIgnoreCase);
            linked.Add(attachment);
        }

        // Rewrite first, sanitize second: the outgoing policy keeps cid: and culls any leftover
        // relative URL, so no staged URL can survive into the wire format.
        var body = _sanitizer.Prepare(html);

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
            var resource = ContentType.TryParse(attachment.Info.ContentType, out var contentType)
                ? await builder.LinkedResources.AddAsync(attachment.Info.FileName, content, contentType, cancellationToken)
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
```

Add the two private helpers (with `using MimeKit.Utils;`):

```csharp
    private static string DomainOf(string address)
    {
        var at = address.LastIndexOf('@');
        return at >= 0 && at < address.Length - 1 ? address[(at + 1)..] : "localhost";
    }

    /// <summary>Threading is best-effort: a malformed id is dropped rather than failing a send.</summary>
    private void ApplyThreadingHeaders(MimeMessage message, SendMessageRequest request)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(request.InReplyTo)) message.InReplyTo = request.InReplyTo;
            foreach (var reference in request.References)
                if (!string.IsNullOrWhiteSpace(reference)) message.References.Add(reference);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Dropped malformed threading headers");
        }
    }
```

- [ ] **Step 4: Run and verify green**

Run: `dotnet test`
Expected: PASS, full suite green (the existing `SendAsync_BuildsFromBodyAndBccAndAppendsSeen` must not regress).

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Models/Mail/SendMessageRequest.cs src/snoopy.microservice/Controllers/MailController.cs src/snoopy.microservice/Services/MailSender.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/MailSenderTests.cs
git commit -F - <<'EOF'
Webmail 2c2b: send with threading headers and multipart/related inline parts
EOF
```

---

### Task 7: Frontend types, api and queries

**Files:**
- Modify: `src/frontend/src/modules/mail/api/mailTypes.ts`, `src/frontend/src/api.js`, `src/frontend/src/modules/mail/queries.ts`

**Interfaces:**
- Produces (consumed by Tasks 8-11): `MailMessageDetail` gains `messageId: string | null`, `references: string[]`, `inReplyTo: string | null`, `replyTo: MailAddressInfo[]`, `bcc: MailAddressInfo[]`; types `StagedAttachmentInfo`, `QuotePurpose`, `PreparedQuote`; `api.prepareQuote(folder, uid, purpose)`; `export function stagedAttachmentUrl(id)`; `usePrepareQuote()` mutation; `SendMessageArgs` gains `inReplyTo?: string`, `references?: string[]`.

- [ ] **Step 1: Implement (type/plumbing task — verified by typecheck + the suites of later tasks)**

`mailTypes.ts` — add to `MailMessageDetail` (after `date`):

```ts
  /** RFC 5322 message id, bare (no angle brackets). Null when the original carries none. */
  messageId: string | null
  /** References chain, oldest first, bare ids. Empty when absent. */
  references: string[]
  inReplyTo: string | null
  replyTo: MailAddressInfo[]
  /** Kept on a Sent copy; empty on received mail. Feeds Edit-as-new. */
  bcc: MailAddressInfo[]
```

and at the end of the file:

```ts
/** One staged outgoing file, as the backend answers it. */
export interface StagedAttachmentInfo {
  id: string
  fileName: string
  size: number
  contentType: string
  /** Non-null marks an inline body resource (cid part) — hidden from the attachment tray. */
  contentId: string | null
}

export type QuotePurpose = 'reply' | 'forward' | 'editAsNew'

/** What PrepareQuote answers: the outbound-sanitised original, cid images rewritten to staged URLs. */
export interface PreparedQuote {
  quotableHtml: string
  attachments: StagedAttachmentInfo[]
}
```

`api.js` — after `deleteAttachment` in the api object:

```js
  prepareQuote: (folder, uid, purpose) =>
    request('POST', '/api/Mail/Messages/PrepareQuote', { folder, uid, purpose }),
```

and beside `mailAttachmentUrl`:

```js
/** Builds a staged attachment's content URL — the src the composer shows inline images through. */
export function stagedAttachmentUrl(id) {
  return `/api/Mail/Attachments/${id}/content`
}
```

`queries.ts` — extend `SendMessageArgs`:

```ts
  /** Threading of a reply/forward: the original's id and its extended references chain. */
  inReplyTo?: string
  references?: string[]
```

and add after `useSendMessage` (import `PreparedQuote`, `QuotePurpose` from `./api/mailTypes`):

```ts
/**
 * Stages the quotable body server-side. A mutation, not a query: every call stages files
 * (a side effect with a TTL), so the result must never be cached or replayed.
 */
export function usePrepareQuote() {
  return useMutation({
    mutationFn: (args: { folder: string; uid: number; purpose: QuotePurpose }) =>
      api.prepareQuote(args.folder, args.uid, args.purpose) as Promise<PreparedQuote>,
  })
}
```

- [ ] **Step 2: Verify**

Run: `npm run typecheck && npm run lint` (from `src/frontend`)
Expected: clean. Then `npm test` — existing suites still green (the new `MailMessageDetail` fields may require existing test fixtures that build a detail object to gain the five fields; update those fixtures with `messageId: null, references: [], inReplyTo: null, replyTo: [], bcc: []`).

- [ ] **Step 3: Commit**

```bash
git add src/frontend/src/modules/mail/api/mailTypes.ts src/frontend/src/api.js src/frontend/src/modules/mail/queries.ts
git commit -F - <<'EOF'
Webmail 2c2b: frontend plumbing for PrepareQuote and threading
EOF
```

(If test fixtures were updated in Step 2, add those files to the same commit.)

---

### Task 8: Pure reply model + threading headers

**Files:**
- Create: `src/frontend/src/modules/mail/compose/replyModel.ts`, `src/frontend/src/modules/mail/compose/threadingHeaders.ts`
- Test: `src/frontend/src/modules/mail/compose/replyModel.test.ts`, `src/frontend/src/modules/mail/compose/threadingHeaders.test.ts`

**Interfaces:**
- Consumes: `MailMessageDetail`, `AliasInfo`, `SendingIdentity` (mailTypes).
- Produces (consumed by Task 9): `myAddresses(primary, aliases) → Set<string>`; `replyRecipients(detail, mine) → { to, cc }`; `replyAllRecipients(detail, mine) → { to, cc }`; `subjectFor('reply' | 'forward', subject) → string`; `preselectIdentity(detail, identities) → string | null`; `editAsNewFrom(detail, identities) → string | null`; `threadingHeaders(detail) → { inReplyTo, references }`.

- [ ] **Step 1: Write the failing tests**

`threadingHeaders.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { threadingHeaders } from './threadingHeaders'

describe('threadingHeaders', () => {
  it('extends the references chain with the message id', () => {
    expect(threadingHeaders({ messageId: 'b@x', references: ['a@x'] }))
      .toEqual({ inReplyTo: 'b@x', references: ['a@x', 'b@x'] })
  })

  it('does not duplicate an id already in the chain', () => {
    expect(threadingHeaders({ messageId: 'a@x', references: ['a@x'] }))
      .toEqual({ inReplyTo: 'a@x', references: ['a@x'] })
  })

  it('leaves the chain alone when the original has no id', () => {
    expect(threadingHeaders({ messageId: null, references: ['a@x'] }))
      .toEqual({ inReplyTo: null, references: ['a@x'] })
  })
})
```

`replyModel.test.ts` — with a `detail(overrides)` helper building a minimal `MailMessageDetail` (all five new fields included):

```ts
import { describe, expect, it } from 'vitest'
import type { MailMessageDetail, SendingIdentity } from '../api/mailTypes'
import {
  editAsNewFrom, myAddresses, preselectIdentity, replyAllRecipients, replyRecipients, subjectFor,
} from './replyModel'

const detail = (overrides: Partial<MailMessageDetail> = {}): MailMessageDetail => ({
  uid: 1, folderPath: 'INBOX', uidValidity: 1, subject: 'Hello',
  fromName: 'Alice', fromAddress: 'alice@ext.example',
  to: [{ name: '', address: 'me@weesky.be' }], cc: [], date: '2026-07-25T10:00:00Z',
  authentication: null, spamScore: null, mailingList: null, sentBy: null, signedBy: null,
  unsubscribeUrl: null, tlsReceived: null, htmlBody: '', textBody: '', blockedImageCount: 0,
  attachments: [], messageId: 'm@x', references: [], inReplyTo: null, replyTo: [], bcc: [],
  ...overrides,
})

const identity = (address: string, overrides: Partial<SendingIdentity> = {}): SendingIdentity => ({
  address, displayName: address, isDefault: false, isPrimary: false, stale: false, labelIsCustom: false,
  ...overrides,
})

const mine = myAddresses('me@weesky.be', [{ name: 'sales', domain: 'weesky.be' }])

describe('myAddresses', () => {
  it('collects the primary and the aliases, lowercased', () => {
    expect(mine).toEqual(new Set(['me@weesky.be', 'sales@weesky.be']))
  })
})

describe('replyRecipients', () => {
  it('targets Reply-To over From', () => {
    const d = detail({ replyTo: [{ name: '', address: 'list@ext.example' }] })
    expect(replyRecipients(d, mine)).toEqual({ to: ['list@ext.example'], cc: [] })
  })

  it('targets From when there is no Reply-To', () => {
    expect(replyRecipients(detail(), mine)).toEqual({ to: ['alice@ext.example'], cc: [] })
  })

  it('replying to my own message targets the original To', () => {
    const d = detail({ fromAddress: 'me@weesky.be', to: [{ name: '', address: 'bob@ext.example' }] })
    expect(replyRecipients(d, mine)).toEqual({ to: ['bob@ext.example'], cc: [] })
  })
})

describe('replyAllRecipients', () => {
  it('keeps the To/Cc split and drops my addresses', () => {
    const d = detail({
      to: [{ name: '', address: 'ME@weesky.be' }, { name: '', address: 'bob@ext.example' }],
      cc: [{ name: '', address: 'carol@ext.example' }, { name: '', address: 'sales@weesky.be' }],
    })
    expect(replyAllRecipients(d, mine)).toEqual({
      to: ['alice@ext.example', 'bob@ext.example'],
      cc: ['carol@ext.example'],
    })
  })

  it('degenerates to a plain reply when everyone is me', () => {
    const d = detail({ fromAddress: 'sales@weesky.be', to: [{ name: '', address: 'me@weesky.be' }] })
    expect(replyAllRecipients(d, mine)).toEqual({ to: ['me@weesky.be'], cc: [] })
  })

  it('promotes Cc to To when To ends empty', () => {
    const d = detail({
      fromAddress: 'me@weesky.be', to: [{ name: '', address: 'sales@weesky.be' }],
      cc: [{ name: '', address: 'carol@ext.example' }],
    })
    expect(replyAllRecipients(d, mine)).toEqual({ to: ['carol@ext.example'], cc: [] })
  })
})

describe('subjectFor', () => {
  it('prefixes and never stacks', () => {
    expect(subjectFor('reply', 'Hello')).toBe('Re: Hello')
    expect(subjectFor('reply', 're: Hello')).toBe('re: Hello')
    expect(subjectFor('forward', 'FW: Hello')).toBe('FW: Hello')
    expect(subjectFor('forward', 'Fwd: Hello')).toBe('Fwd: Hello')
    expect(subjectFor('reply', 'Fwd: Hello')).toBe('Re: Fwd: Hello')
  })
})

describe('preselectIdentity', () => {
  const identities = [identity('me@weesky.be', { isDefault: true }), identity('sales@weesky.be')]

  it('picks the first of my identities found in To then Cc', () => {
    const d = detail({
      to: [{ name: '', address: 'other@ext.example' }],
      cc: [{ name: '', address: 'SALES@weesky.be' }],
    })
    expect(preselectIdentity(d, identities)).toBe('sales@weesky.be')
  })

  it('falls back to the default and never offers a stale identity', () => {
    const withStale = [identity('me@weesky.be', { isDefault: true }), identity('gone@weesky.be', { stale: true })]
    const d = detail({ to: [{ name: '', address: 'gone@weesky.be' }] })
    expect(preselectIdentity(d, withStale)).toBe('me@weesky.be')
  })
})

describe('editAsNewFrom', () => {
  it("uses the original's From when it is one of my identities, else the default", () => {
    const identities = [identity('me@weesky.be', { isDefault: true }), identity('sales@weesky.be')]
    expect(editAsNewFrom(detail({ fromAddress: 'Sales@weesky.be' }), identities)).toBe('sales@weesky.be')
    expect(editAsNewFrom(detail(), identities)).toBe('me@weesky.be')
  })
})
```

- [ ] **Step 2: Run and verify they fail**

Run: `npx vitest run src/modules/mail/compose/replyModel.test.ts src/modules/mail/compose/threadingHeaders.test.ts`
Expected: FAIL — modules do not exist.

- [ ] **Step 3: Implement**

`threadingHeaders.ts`:

```ts
import type { MailMessageDetail } from '../api/mailTypes'

export interface ThreadingHeaders { inReplyTo: string | null; references: string[] }

/**
 * RFC 5322 threading: the new message replies to the original (In-Reply-To) and extends its
 * References chain. Same computation for reply, reply-all and forward; edit-as-new sends none.
 */
export function threadingHeaders(
  detail: Pick<MailMessageDetail, 'messageId' | 'references'>,
): ThreadingHeaders {
  if (!detail.messageId) return { inReplyTo: null, references: [...detail.references] }
  const references = detail.references.includes(detail.messageId)
    ? [...detail.references]
    : [...detail.references, detail.messageId]
  return { inReplyTo: detail.messageId, references }
}
```

`replyModel.ts`:

```ts
import type { AliasInfo, MailAddressInfo, MailMessageDetail, SendingIdentity } from '../api/mailTypes'

export interface Recipients { to: string[]; cc: string[] }

/** Canonical set of the account's own addresses: primary + live aliases, lowercased. */
export function myAddresses(primary: string | null | undefined, aliases: AliasInfo[]): Set<string> {
  const mine = new Set<string>()
  if (primary) mine.add(primary.toLowerCase())
  for (const alias of aliases) mine.add(`${alias.name}@${alias.domain}`.toLowerCase())
  return mine
}

const isMine = (mine: Set<string>, address: string) => mine.has(address.toLowerCase())
const addressesOf = (list: MailAddressInfo[]) => list.map(a => a.address)

function dedupe(addresses: string[]): string[] {
  const seen = new Set<string>()
  const out: string[] = []
  for (const address of addresses) {
    const key = address.toLowerCase()
    if (!address || seen.has(key)) continue
    seen.add(key)
    out.push(address)
  }
  return out
}

/** The reply target: Reply-To when present, From otherwise. */
function senderOf(detail: MailMessageDetail): string[] {
  return detail.replyTo.length > 0 ? addressesOf(detail.replyTo) : (detail.fromAddress ? [detail.fromAddress] : [])
}

/**
 * Reply targets the sender — unless the sender is me (replying to my own Sent copy), where the
 * expected gesture is nudging the thread: the original To is the target instead.
 */
export function replyRecipients(detail: MailMessageDetail, mine: Set<string>): Recipients {
  const sender = senderOf(detail)
  if (sender.length > 0 && sender.every(a => isMine(mine, a))) {
    const to = dedupe(addressesOf(detail.to))
    if (to.length > 0) return { to, cc: [] }
  }
  return { to: dedupe(sender), cc: [] }
}

/**
 * Reply-all keeps the original To/Cc split (the mainstream shape): To = sender + original To
 * minus my addresses, Cc = original Cc minus mine. All-mine degenerates to a plain reply, and
 * a Cc-only remainder is promoted to To — a message cannot send without one.
 */
export function replyAllRecipients(detail: MailMessageDetail, mine: Set<string>): Recipients {
  const to = dedupe([...senderOf(detail), ...addressesOf(detail.to)].filter(a => !isMine(mine, a)))
  const inTo = new Set(to.map(a => a.toLowerCase()))
  const cc = dedupe(addressesOf(detail.cc).filter(a => !isMine(mine, a) && !inTo.has(a.toLowerCase())))
  if (to.length === 0 && cc.length === 0) return replyRecipients(detail, mine)
  if (to.length === 0) return { to: cc, cc: [] }
  return { to, cc }
}

/** Re:/Fwd: without stacking — an already-prefixed subject keeps its single prefix. */
export function subjectFor(purpose: 'reply' | 'forward', subject: string): string {
  const trimmed = subject.trim()
  const wanted = purpose === 'reply' ? /^re\s*:/i : /^fwd?\s*:/i
  if (wanted.test(trimmed)) return trimmed
  return `${purpose === 'reply' ? 'Re' : 'Fwd'}: ${trimmed}`
}

/**
 * The identity a reply opens with: the first usable identity found among the original's To then
 * Cc (an owned address without an identity cannot appear in the From menu), else the default.
 */
export function preselectIdentity(detail: MailMessageDetail, identities: SendingIdentity[]): string | null {
  const usable = identities.filter(i => !i.stale)
  const byAddress = new Map(usable.map(i => [i.address.toLowerCase(), i.address]))
  for (const recipient of [...detail.to, ...detail.cc]) {
    const found = byAddress.get(recipient.address.toLowerCase())
    if (found) return found
  }
  return usable.find(i => i.isDefault)?.address ?? null
}

/** Edit-as-new opens from the original's From when it is one of my identities, else the default. */
export function editAsNewFrom(detail: MailMessageDetail, identities: SendingIdentity[]): string | null {
  const usable = identities.filter(i => !i.stale)
  const match = usable.find(i => i.address.toLowerCase() === detail.fromAddress.toLowerCase())
  return match?.address ?? usable.find(i => i.isDefault)?.address ?? null
}
```

- [ ] **Step 4: Run and verify green**

Run: `npx vitest run src/modules/mail/compose/replyModel.test.ts src/modules/mail/compose/threadingHeaders.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/compose/replyModel.ts src/frontend/src/modules/mail/compose/replyModel.test.ts src/frontend/src/modules/mail/compose/threadingHeaders.ts src/frontend/src/modules/mail/compose/threadingHeaders.test.ts
git commit -F - <<'EOF'
Webmail 2c2b: pure reply model and threading headers
EOF
```

---

### Task 9: Quote assembly + the compose seed builder

**Files:**
- Create: `src/frontend/src/modules/mail/compose/quote.ts`, `src/frontend/src/modules/mail/compose/composeSeed.ts`
- Test: `src/frontend/src/modules/mail/compose/quote.test.ts`, `src/frontend/src/modules/mail/compose/composeSeed.test.ts`

**Interfaces:**
- Consumes: Task 8's functions, `formatReaderDate(dateString)` (`reader/formatReaderDate.ts`), `PreparedQuote` / `StagedAttachmentInfo` (Task 7).
- Produces (consumed by Tasks 10-11): `replyQuote(quotableHtml, { dateText, name, address })`; `forwardQuote(quotableHtml, { fromName, fromAddress, dateText, subject, to })`; `type ComposeAction = 'reply' | 'replyAll' | 'forward' | 'editAsNew'`; `interface ComposeSeed { to; cc; bcc; subject; html; fromAddress; attachments: StagedAttachmentInfo[]; inReplyTo; references }`; `buildComposeSeed(action, detail, prepared, identities, aliases, primaryAddress) → ComposeSeed`. (The seed carries full `StagedAttachmentInfo` objects, not bare ids — the tray needs names/sizes and the inline/regular split needs `contentId`.)

- [ ] **Step 1: Write the failing tests**

`quote.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { forwardQuote, replyQuote } from './quote'

describe('replyQuote', () => {
  it('puts a cursor line, the attribution, then the blockquote', () => {
    const html = replyQuote('<p>original</p>', { dateText: '25 Jul 2026', name: 'Alice', address: 'a@x' })
    expect(html).toBe(
      '<div><br></div><div>On 25 Jul 2026, Alice &lt;a@x&gt; wrote:</div><blockquote><p>original</p></blockquote>')
  })

  it('escapes the attribution and drops an empty name', () => {
    const html = replyQuote('<p>o</p>', { dateText: 'd', name: '', address: 'a<b@x' })
    expect(html).toContain('On d, a&lt;b@x wrote:')
  })
})

describe('forwardQuote', () => {
  it('builds the banner and headers before the original', () => {
    const html = forwardQuote('<p>original</p>', {
      fromName: 'Alice', fromAddress: 'a@x', dateText: '25 Jul 2026', subject: 'Hi <you>', to: ['b@x', 'c@x'],
    })
    expect(html).toContain('---------- Forwarded message ----------')
    expect(html).toContain('From: Alice &lt;a@x&gt;')
    expect(html).toContain('Date: 25 Jul 2026')
    expect(html).toContain('Subject: Hi &lt;you&gt;')
    expect(html).toContain('To: b@x, c@x')
    expect(html.startsWith('<div><br></div>')).toBe(true)
    expect(html.endsWith('<p>original</p>')).toBe(true)
  })
})
```

`composeSeed.test.ts` — reuse the same `detail()` / `identity()` helpers as `replyModel.test.ts` (duplicate them in the file; a test helper module is not worth the indirection here):

```ts
import { describe, expect, it } from 'vitest'
import { buildComposeSeed } from './composeSeed'
// … detail() / identity() helpers as in replyModel.test.ts …

const prepared = {
  quotableHtml: '<p>original</p>',
  attachments: [
    { id: 'i1', fileName: 'logo.png', size: 3, contentType: 'image/png', contentId: 'logo@x' },
    { id: 'a1', fileName: 'doc.pdf', size: 9, contentType: 'application/pdf', contentId: null },
  ],
}
const identities = [identity('me@weesky.be', { isDefault: true })]
const aliases = [{ name: 'sales', domain: 'weesky.be' }]

describe('buildComposeSeed', () => {
  it('reply: recipients, Re: subject, quoted body, threading', () => {
    const seed = buildComposeSeed('reply', detail(), prepared, identities, aliases, 'me@weesky.be')
    expect(seed.to).toEqual(['alice@ext.example'])
    expect(seed.subject).toBe('Re: Hello')
    expect(seed.html).toContain('<blockquote><p>original</p></blockquote>')
    expect(seed.inReplyTo).toBe('m@x')
    expect(seed.references).toEqual(['m@x'])
    expect(seed.fromAddress).toBe('me@weesky.be')
    expect(seed.attachments).toEqual(prepared.attachments)
  })

  it('forward: empty recipients, Fwd: subject, banner body, threading', () => {
    const seed = buildComposeSeed('forward', detail(), prepared, identities, aliases, 'me@weesky.be')
    expect(seed.to).toEqual([])
    expect(seed.subject).toBe('Fwd: Hello')
    expect(seed.html).toContain('---------- Forwarded message ----------')
    expect(seed.inReplyTo).toBe('m@x')
  })

  it('editAsNew: original recipients and subject, bare body, no threading', () => {
    const d = detail({ bcc: [{ name: '', address: 'hidden@ext.example' }] })
    const seed = buildComposeSeed('editAsNew', d, prepared, identities, aliases, 'me@weesky.be')
    expect(seed.to).toEqual(['me@weesky.be'])
    expect(seed.bcc).toEqual(['hidden@ext.example'])
    expect(seed.subject).toBe('Hello')
    expect(seed.html).toBe('<p>original</p>')
    expect(seed.inReplyTo).toBeNull()
    expect(seed.references).toEqual([])
  })
})
```

- [ ] **Step 2: Run and verify they fail**

Run: `npx vitest run src/modules/mail/compose/quote.test.ts src/modules/mail/compose/composeSeed.test.ts`
Expected: FAIL — modules do not exist.

- [ ] **Step 3: Implement**

`quote.ts`:

```ts
const escapeHtml = (text: string) =>
  text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')

const who = (name: string, address: string) =>
  name ? `${escapeHtml(name)} &lt;${escapeHtml(address)}&gt;` : escapeHtml(address)

export interface Attribution { dateText: string; name: string; address: string }

/** Reply body: an empty cursor line, the attribution, then the original inside a visible blockquote. */
export function replyQuote(quotableHtml: string, attribution: Attribution): string {
  const { dateText, name, address } = attribution
  return `<div><br></div><div>On ${escapeHtml(dateText)}, ${who(name, address)} wrote:</div>`
    + `<blockquote>${quotableHtml}</blockquote>`
}

export interface ForwardHeader {
  fromName: string; fromAddress: string; dateText: string; subject: string; to: string[]
}

/** Forward body: cursor line, the forwarded-message banner and headers, then the original. */
export function forwardQuote(quotableHtml: string, header: ForwardHeader): string {
  const lines = [
    '---------- Forwarded message ----------',
    `From: ${who(header.fromName, header.fromAddress)}`,
    `Date: ${escapeHtml(header.dateText)}`,
    `Subject: ${escapeHtml(header.subject)}`,
    `To: ${header.to.map(escapeHtml).join(', ')}`,
  ]
  return `<div><br></div>${lines.map(l => `<div>${l}</div>`).join('')}<div><br></div>${quotableHtml}`
}
```

`composeSeed.ts`:

```ts
import type {
  AliasInfo, MailMessageDetail, PreparedQuote, SendingIdentity, StagedAttachmentInfo,
} from '../api/mailTypes'
import { formatReaderDate } from '../reader/formatReaderDate'
import {
  editAsNewFrom, myAddresses, preselectIdentity, replyAllRecipients, replyRecipients, subjectFor,
} from './replyModel'
import { threadingHeaders } from './threadingHeaders'
import { forwardQuote, replyQuote } from './quote'

export type ComposeAction = 'reply' | 'replyAll' | 'forward' | 'editAsNew'

/** Everything a prefilled composer opens with — the shape a 2c3 draft will also take. */
export interface ComposeSeed {
  to: string[]
  cc: string[]
  bcc: string[]
  subject: string
  html: string
  fromAddress: string | null
  attachments: StagedAttachmentInfo[]
  inReplyTo: string | null
  references: string[]
}

/** One place turns an original plus its prepared quote into a composer seed. Pure. */
export function buildComposeSeed(
  action: ComposeAction,
  detail: MailMessageDetail,
  prepared: PreparedQuote,
  identities: SendingIdentity[],
  aliases: AliasInfo[],
  primaryAddress: string | null,
): ComposeSeed {
  const dateText = formatReaderDate(detail.date)

  if (action === 'editAsNew') {
    return {
      to: detail.to.map(a => a.address),
      cc: detail.cc.map(a => a.address),
      bcc: detail.bcc.map(a => a.address),
      subject: detail.subject,
      html: prepared.quotableHtml,
      fromAddress: editAsNewFrom(detail, identities),
      attachments: prepared.attachments,
      inReplyTo: null,
      references: [],
    }
  }

  const threading = threadingHeaders(detail)
  const fromAddress = preselectIdentity(detail, identities)

  if (action === 'forward') {
    return {
      to: [], cc: [], bcc: [],
      subject: subjectFor('forward', detail.subject),
      html: forwardQuote(prepared.quotableHtml, {
        fromName: detail.fromName, fromAddress: detail.fromAddress,
        dateText, subject: detail.subject, to: detail.to.map(a => a.address),
      }),
      fromAddress,
      attachments: prepared.attachments,
      inReplyTo: threading.inReplyTo,
      references: threading.references,
    }
  }

  const mine = myAddresses(primaryAddress, aliases)
  const recipients = action === 'reply' ? replyRecipients(detail, mine) : replyAllRecipients(detail, mine)
  return {
    to: recipients.to, cc: recipients.cc, bcc: [],
    subject: subjectFor('reply', detail.subject),
    html: replyQuote(prepared.quotableHtml, { dateText, name: detail.fromName, address: detail.fromAddress }),
    fromAddress,
    attachments: prepared.attachments,
    inReplyTo: threading.inReplyTo,
    references: threading.references,
  }
}
```

- [ ] **Step 4: Run and verify green**

Run: `npx vitest run src/modules/mail/compose/quote.test.ts src/modules/mail/compose/composeSeed.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/compose/quote.ts src/frontend/src/modules/mail/compose/quote.test.ts src/frontend/src/modules/mail/compose/composeSeed.ts src/frontend/src/modules/mail/compose/composeSeed.test.ts
git commit -F - <<'EOF'
Webmail 2c2b: quote assembly and the compose seed builder
EOF
```

---

### Task 10: The seeded ComposeView

**Files:**
- Modify: `src/frontend/src/modules/mail/compose/SquireEditor.tsx`, `src/frontend/src/modules/mail/compose/useStagedAttachments.ts`, `src/frontend/src/modules/mail/compose/ComposeView.tsx`
- Test: `src/frontend/src/modules/mail/compose/ComposeView.test.tsx`, `src/frontend/src/modules/mail/compose/useStagedAttachments.test.tsx`

**Interfaces:**
- Consumes: `ComposeSeed` (Task 9), `SendMessageArgs.inReplyTo/references` (Task 7), `api.deleteAttachment` (exists).
- Produces: `SquireEditor` prop `initialHtml?: string`; `useStagedAttachments(initial?: { id; fileName; size }[])`; `ComposeView` reads `location.state.seed` as `ComposeSeed | undefined`.

- [ ] **Step 1: Write the failing tests**

`useStagedAttachments.test.tsx` — add:

```tsx
it('seeds already-staged items as completed uploads', () => {
  const { result } = renderHook(() =>
    useStagedAttachments([{ id: 'a1', fileName: 'doc.pdf', size: 9 }]))

  expect(result.current.items).toHaveLength(1)
  expect(result.current.items[0]).toMatchObject({ id: 'a1', fileName: 'doc.pdf', progress: 1, error: null })
  expect(result.current.ids).toEqual(['a1'])
  expect(result.current.uploading).toBe(false)
})
```

`ComposeView.test.tsx` — add a seeded-render describe block, following the file's existing render helper (router + query client + api mocks). Render with the seed in the router entry state:

```tsx
const seed = {
  to: ['alice@ext.example'], cc: ['bob@ext.example'], bcc: [],
  subject: 'Re: Hello', html: '<div><br></div><blockquote><p>original</p></blockquote>',
  fromAddress: null,
  attachments: [
    { id: 'i1', fileName: 'logo.png', size: 3, contentType: 'image/png', contentId: 'logo@x' },
    { id: 'a1', fileName: 'doc.pdf', size: 9, contentType: 'application/pdf', contentId: null },
  ],
  inReplyTo: 'm@x', references: ['m@x'],
}
// Router entry: { pathname: '/mail/compose', state: { from: 'INBOX', seed } }
```

Assertions (one `it` each):
1. *prefills the form*: the To token `alice@ext.example` is shown, the Cc row is open showing `bob@ext.example`, the subject input has value `Re: Hello`.
2. *seeds only regular attachments into the tray*: `doc.pdf` visible, `logo.png` absent.
3. *sends the threading and every staged id*: click Send; the `api.sendMessage` mock received `expect.objectContaining({ inReplyTo: 'm@x', references: ['m@x'], attachmentIds: expect.arrayContaining(['i1', 'a1']) })`.
4. *a seeded composer is dirty from the start*: navigating away (the file's existing discard-flow test pattern) shows the Discard dialog without touching any field.

- [ ] **Step 2: Run and verify they fail**

Run: `npx vitest run src/modules/mail/compose/ComposeView.test.tsx src/modules/mail/compose/useStagedAttachments.test.tsx`
Expected: FAIL — no seed support yet.

- [ ] **Step 3: Implement**

`SquireEditor.tsx` — `interface Props` gains `initialHtml?: string`; in the mount effect, after `editor.current = squire`:

```ts
    if (initialHtml) {
      // Passes through sanitizeToDOMFragment like every setHTML; the caller's quote/seed markup
      // is treated as untrusted input, same as a paste.
      squire.setHTML(initialHtml)
      squire.moveCursorToStart()
    }
```

(`initialHtml` is read once at mount by design — extend the existing "Mount once" comment.)

`useStagedAttachments.ts`:

```ts
export function useStagedAttachments(initial: { id: string; fileName: string; size: number }[] = []) {
  const [items, setItems] = useState<StagedItem[]>(() => initial.map(item => ({
    key: `staged-${nextKey++}`, id: item.id, fileName: item.fileName, size: item.size, progress: 1, error: null,
  })))
```

(`itemsRef` initialisation already follows `items`.)

`ComposeView.tsx`:

```ts
import { api } from '../../../api.js'                       // extend the existing import if present
import type { ComposeSeed } from './composeSeed'

  const state = location.state as { from?: string; seed?: ComposeSeed } | null
  const seed = state?.seed ?? null

  const [to, setTo] = useState<string[]>(seed?.to ?? [])
  const [cc, setCc] = useState<string[]>(seed?.cc ?? [])
  const [bcc, setBcc] = useState<string[]>(seed?.bcc ?? [])
  const [showCc, setShowCc] = useState((seed?.cc.length ?? 0) > 0)
  const [showBcc, setShowBcc] = useState((seed?.bcc.length ?? 0) > 0)
  const [subject, setSubject] = useState(seed?.subject ?? '')
  // A seeded body is content the user can lose — dirty from the first render.
  const [bodyTouched, setBodyTouched] = useState(Boolean(seed?.html))
  const [fromAddress, setFromAddress] = useState<string | null>(seed?.fromAddress ?? null)
  // Inline resources live in the body, not the tray; their ids still ride the send payload.
  const seedTray = useMemo(() => (seed?.attachments ?? []).filter(a => !a.contentId), [seed])
  const inlineIds = useMemo(
    () => (seed?.attachments ?? []).filter(a => a.contentId).map(a => a.id), [seed])
  const attachments = useStagedAttachments(seedTray)
```

(`location` must be read before the state hooks — move the existing `const from = (location.state …)` extraction up beside `seed`, keeping `backTarget` unchanged.)

`submit()` payload gains:

```ts
        attachmentIds: [...inlineIds, ...attachments.ids],
        inReplyTo: seed?.inReplyTo ?? undefined,
        references: seed?.references && seed.references.length > 0 ? seed.references : undefined,
```

Discard button (the blocker modal) also releases the inline resources:

```ts
                onClick={() => {
                  attachments.discardAll()
                  inlineIds.forEach(id => { api.deleteAttachment(id).catch(() => { /* sweeper's problem */ }) })
                  leavingRef.current = true
                  blocker.proceed?.()
                }}
```

Editor mount:

```tsx
      <SquireEditor ref={setEditor} initialHtml={seed?.html} onChange={touchBody} onFormatChange={setActive} />
```

- [ ] **Step 4: Run and verify green**

Run: `npx vitest run src/modules/mail/compose/` then `npm run typecheck && npm run lint`
Expected: PASS / clean. Then `npm test` — full suite green.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/compose/SquireEditor.tsx src/frontend/src/modules/mail/compose/useStagedAttachments.ts src/frontend/src/modules/mail/compose/ComposeView.tsx src/frontend/src/modules/mail/compose/ComposeView.test.tsx src/frontend/src/modules/mail/compose/useStagedAttachments.test.tsx
git commit -F - <<'EOF'
Webmail 2c2b: the composer opens from a seed

Router-state seed prefills recipients, subject, quoted body, identity,
staged parts and threading; inline resources stay out of the tray.
EOF
```

---

### Task 11: Reader actions — Reply / Reply all / Forward / Edit as new

**Files:**
- Create: `src/frontend/src/icons/ReplyIcon.tsx`, `src/frontend/src/icons/ReplyAllIcon.tsx`, `src/frontend/src/icons/ForwardIcon.tsx`
- Modify: `src/frontend/src/modules/mail/reader/ReaderActions.tsx`, `src/frontend/src/modules/mail/reader/MessageReader.tsx`
- Test: `src/frontend/src/modules/mail/reader/ReaderActions.test.tsx`, `src/frontend/src/modules/mail/reader/MessageReader.test.tsx`

**Interfaces:**
- Consumes: `usePrepareQuote()` (Task 7), `buildComposeSeed` / `ComposeAction` (Task 9), `useIdentities()` / `useAliases()` (exist in `queries.ts`), `useAuth()` (`identity.email` is the primary address — same source `ComposeView` uses), `PencilIcon` (exists).
- Produces: `ReaderActions` props gain `onReply: () => void`, `onReplyAll: () => void`, `onForward: () => void`, `preparing: boolean`. Edit-as-new rides the existing `actions: MenuEntry[]` from `MessageReader` (kebab menu) — no new kebab prop.

- [ ] **Step 1: Write the failing tests**

`ReaderActions.test.tsx` — follow the file's existing props fixture; add the three new props to it, then:

```tsx
it('fires the three quote actions and disables them while preparing', () => {
  const onReply = vi.fn(); const onReplyAll = vi.fn(); const onForward = vi.fn()
  render(<ReaderActions {...baseProps} onReply={onReply} onReplyAll={onReplyAll} onForward={onForward} preparing={false} />)

  fireEvent.click(screen.getByRole('button', { name: 'Reply' }))
  fireEvent.click(screen.getByRole('button', { name: 'Reply all' }))
  fireEvent.click(screen.getByRole('button', { name: 'Forward' }))
  expect(onReply).toHaveBeenCalledOnce()
  expect(onReplyAll).toHaveBeenCalledOnce()
  expect(onForward).toHaveBeenCalledOnce()
})

it('disables the quote actions while a preparation is pending', () => {
  render(<ReaderActions {...baseProps} onReply={vi.fn()} onReplyAll={vi.fn()} onForward={vi.fn()} preparing />)
  expect(screen.getByRole('button', { name: 'Reply' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Forward' })).toBeDisabled()
})
```

`MessageReader.test.tsx` — follow the file's existing render harness (query client + `api` mocks + router). Add:

1. *Reply prepares and navigates seeded*: mock `api.prepareQuote` to resolve `{ quotableHtml: '<p>o</p>', attachments: [] }`; click the `Reply` button; assert `api.prepareQuote` was called with `(folderPath, uid, 'reply')` and the router navigated to `/mail/compose` with `state.seed.subject` starting `Re:` (the harness's `MemoryRouter` can render a probe route for `/mail/compose` that prints `location.state`).
2. *Edit as new lives in the kebab*: open the kebab menu, assert an `Edit as new` entry; select it; assert `api.prepareQuote` called with purpose `editAsNew`.
3. *a failed preparation notifies and stays*: mock `api.prepareQuote` to reject with `new Error('over the cap')`; click `Forward`; assert `onNotify` received `'over the cap'` and no navigation happened.

- [ ] **Step 2: Run and verify they fail**

Run: `npx vitest run src/modules/mail/reader/ReaderActions.test.tsx src/modules/mail/reader/MessageReader.test.tsx`
Expected: FAIL — props and buttons do not exist.

- [ ] **Step 3: Implement**

Icons — copy the component shell conventions from `src/icons/ArrowLeftIcon.tsx` (same stroke attributes, `size = 20` default). Feather-style paths:

```tsx
// ReplyIcon.tsx — corner-up-left
<polyline points="9 14 4 9 9 4" />
<path d="M20 20v-7a4 4 0 0 0-4-4H4" />

// ReplyAllIcon.tsx — doubled corner-up-left
<polyline points="7 14 2 9 7 4" />
<polyline points="12 14 7 9 12 4" />
<path d="M22 20v-7a4 4 0 0 0-4-4H7" />

// ForwardIcon.tsx — corner-up-right
<polyline points="15 14 20 9 15 4" />
<path d="M4 20v-7a4 4 0 0 1 4-4h12" />
```

`ReaderActions.tsx` — extend `Props`:

```ts
  onReply: () => void
  onReplyAll: () => void
  onForward: () => void
  /** A PrepareQuote round-trip is in flight — the quote actions hold until it lands. */
  preparing: boolean
```

Render at the start of `.reader-actions` (before the colour toggle), mail-surface hover language (`action-btn`, glyph-only recolour):

```tsx
      <button type="button" className="action-btn" aria-label="Reply" title="Reply"
        disabled={preparing} onClick={onReply}>
        <ReplyIcon size={18} />
      </button>
      <button type="button" className="action-btn" aria-label="Reply all" title="Reply all"
        disabled={preparing} onClick={onReplyAll}>
        <ReplyAllIcon size={18} />
      </button>
      <button type="button" className="action-btn" aria-label="Forward" title="Forward"
        disabled={preparing} onClick={onForward}>
        <ForwardIcon size={18} />
      </button>
      <span className="actions-rule" />
```

`MessageReader.tsx`:

```ts
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../../../contexts/AuthContext'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import { useAliases, useIdentities, usePrepareQuote } from '../queries'   // extend the existing import
import { buildComposeSeed, type ComposeAction } from '../compose/composeSeed'
```

Inside the component (with the other hooks — hooks must sit above the early returns):

```ts
  const navigate = useNavigate()
  const { identity } = useAuth()
  const { data: identityList } = useIdentities()
  const { data: aliases } = useAliases()
  const prepare = usePrepareQuote()
```

After the `moveTo`/`onDelete` helpers:

```ts
  async function openCompose(action: ComposeAction) {
    try {
      const purpose = action === 'editAsNew' ? 'editAsNew' : action === 'forward' ? 'forward' : 'reply'
      const prepared = await prepare.mutateAsync({ folder: folderPath!, uid: uid!, purpose })
      const seed = buildComposeSeed(
        action, data!, prepared, identityList ?? [], aliases ?? [], identity?.email ?? null)
      navigate('/mail/compose', { state: { from: folderPath, seed } })
    } catch (error) {
      onNotify?.(error instanceof Error ? error.message : 'Could not prepare the message')
    }
  }
```

Append to the `actions` array (after `Copy to…`):

```ts
    { label: 'Edit as new', icon: <PencilIcon />, onSelect: () => void openCompose('editAsNew') },
```

And on `<ReaderActions …>`:

```tsx
          onReply={() => void openCompose('reply')}
          onReplyAll={() => void openCompose('replyAll')}
          onForward={() => void openCompose('forward')}
          preparing={prepare.isPending}
```

- [ ] **Step 4: Run and verify green**

Run: `npx vitest run src/modules/mail/reader/` then `npm test && npm run typecheck && npm run lint && npm run build`
Expected: all green/clean.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/icons/ReplyIcon.tsx src/frontend/src/icons/ReplyAllIcon.tsx src/frontend/src/icons/ForwardIcon.tsx src/frontend/src/modules/mail/reader/ReaderActions.tsx src/frontend/src/modules/mail/reader/MessageReader.tsx src/frontend/src/modules/mail/reader/ReaderActions.test.tsx src/frontend/src/modules/mail/reader/MessageReader.test.tsx
git commit -F - <<'EOF'
Webmail 2c2b: Reply, Reply all, Forward buttons and Edit as new
EOF
```

---

### Task 12: Full verification

**Files:** none modified (fix regressions where they live if any turn up).

- [ ] **Step 1: Backend** — from `src/snoopy.microservice`: `dotnet build -c Release && dotnet test`. Expected: build clean, all tests green.
- [ ] **Step 2: Frontend** — from `src/frontend`: `npm test && npm run lint && npm run build`. Expected: all green, lint clean, build clean.
- [ ] **Step 3:** Report the counts. Manual verification on `dev` (spec § 7 — reply threading in a third-party client, reply-all self-exclusion, forward with attachment + inline image, edit-as-new from Sent with Bcc) is the human's checklist after deployment, not part of this plan.

---

## Self-Review Notes

- Spec § 3.1 → Task 1; § 3.2 → Tasks 4-5; § 3.3-3.4 → Task 3; § 3.5 → Task 6; § 3.6 → Task 2; § 4.1 → Tasks 8-9; § 4.2 → Task 11; § 4.3 → Task 10; § 4.4 → Task 7; § 5 covered task-by-task; edit-as-new (§ 1, 2, 4) → Tasks 4-5 (purpose), 8-9 (seed), 11 (kebab).
- Deviation from the spec's `ComposeSeed` sketch: the seed carries `attachments: StagedAttachmentInfo[]` instead of `attachmentIds: string[]` — the tray needs names/sizes and the inline/regular split needs `contentId`. Noted in Task 9's Interfaces block.
- `useSendMessage` needs no change beyond `SendMessageArgs`: the payload passes through `api.sendMessage` verbatim.
