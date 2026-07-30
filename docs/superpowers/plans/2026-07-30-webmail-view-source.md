# View source Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A "View source" entry at the foot of the reader's kebab opens `/mail/source?folder=…&uid=…` in a new browser tab — a chrome-less page carrying a synthesis table above the verbatim RFC822 source.

**Architecture:** A new `GET /api/Mail/Messages/Source` returns the synthesis and the source in one answer, fetching only the first megabyte from IMAP via a `BODY[]<0.N>` partial fetch rather than pulling a whole 25 MB message. On the client the page is a route **sibling** of `AppShell` under `RequireAuth`, which is what removes the rail and the folder column — and with them `useFolders`'s 60-second poll. The kebab entry is a real `<a target="_blank">`, so middle-click and Ctrl+click work.

**Tech Stack:** ASP.NET (.NET 10), MailKit 4.17.0, MimeKit, xUnit + Moq · React 18 + TypeScript, react-router-dom v6, TanStack Query, Vitest + jsdom + @testing-library/react.

**Spec:** `docs/superpowers/specs/2026-07-30-webmail-view-source-design.md`

## Global Constraints

- **The cap is 1 MB**, written once as `MaxSourceBytes = 1024 * 1024` in `MailController`. An internal constant, never a setting.
- **`Truncated` is `TotalBytes > MaxSourceBytes`**, computed from the IMAP-reported size — never inferred from the length of what came back. A message of exactly 1 MB is complete.
- **The source is rendered as text, never as markup.** No `dangerouslySetInnerHTML`, no iframe, no DOMPurify pass on this content.
- **`authVerdict` and `AuthBadge` are not touched.** The badge's green-on-SPF-and-DKIM rule is a validated decision; DMARC does not enter it.
- **Every new icon carries `stroke="currentColor"`** — `src/icons/icons.test.tsx` asserts it across the whole set and a new icon must be added to that file's imports and list.
- **No "Download original", no "Copy to clipboard", no back button, no logo** on the source page. Closing the tab is the return.
- Backend commands run from `src/snoopy.microservice`, frontend commands from `src/frontend`.
- Run `dotnet test` (never `--no-build`) whenever a task adds a test file.

---

### Task 1: The DMARC verdict

**Files:**
- Modify: `src/snoopy.microservice/Models/Mail/MailAuthentication.cs`
- Modify: `src/snoopy.microservice/Services/MailAuthenticationReader.cs:17-19`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/MailAuthenticationReaderTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `MailAuthentication(string? Spf, string? Dkim, string? Dmarc, string Raw)` — a **positional record**, so the parameter order is the constructor's; every existing construction site must gain the third argument. `MailMessageDetail.Authentication` picks the new field up with no change of its own.

- [ ] **Step 1: Write the failing tests**

`MailAuthenticationReaderTests.cs` already builds a header carrying `dmarc=pass`. Add the assertion to the existing `Parse_ReadsBothVerdictsFromARealHeader` and add one new test below it:

```csharp
    [Fact]
    public void Parse_ReadsTheDmarcVerdict()
    {
        const string header = "mx.google.com; spf=pass smtp.mailfrom=a@b.test; " +
                              "dkim=pass header.i=@b.test; dmarc=fail header.from=b.test";

        var result = MailAuthenticationReader.Parse(Headers(header));

        Assert.Equal("fail", result!.Dmarc);
    }

    [Fact]
    public void Parse_LeavesDmarcNullWhenTheHeaderCarriesNone()
    {
        var result = MailAuthenticationReader.Parse(Headers("mx.google.com; spf=pass smtp.mailfrom=a@b.test"));

        Assert.Null(result!.Dmarc);
    }
```

And in `Parse_ReadsBothVerdictsFromARealHeader`, after the `result.Dkim` assertion:

```csharp
        Assert.Equal("pass", result.Dmarc);
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter MailAuthenticationReaderTests`
Expected: FAIL — `MailAuthentication` has no `Dmarc` member (compile error).

- [ ] **Step 3: Add the field to the record**

`Models/Mail/MailAuthentication.cs`, replacing the whole record:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>SPF, DKIM and DMARC verdicts as the receiving server reported them, plus the header they came from.</summary>
public sealed record MailAuthentication(string? Spf, string? Dkim, string? Dmarc, string Raw);
```

- [ ] **Step 4: Read the third verdict**

`Services/MailAuthenticationReader.cs`, in `Parse`, replace the return statement:

```csharp
        return AuthenticationResults.TryParse(header.RawValue, out var parsed)
            ? new MailAuthentication(
                Verdict(parsed.Results, "spf"),
                Verdict(parsed.Results, "dkim"),
                Verdict(parsed.Results, "dmarc"),
                header.Value)
            : new MailAuthentication(null, null, null, header.Value);
```

`Verdict` is untouched: it already takes the method name and already collapses repeated occurrences the same way for any method.

- [ ] **Step 5: Fix every other construction site**

Run: `dotnet build 2>&1 | grep -i "MailAuthentication"`

Every remaining error is a `new MailAuthentication(a, b, c)` needing a `null` (or the real verdict) inserted before the last argument. Fix each; do not add a parameterless overload to avoid the work — a positional record with a silently-defaulted verdict is how a field stays null forever.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter MailAuthenticationReaderTests`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/snoopy.microservice
git commit -F - <<'EOF'
Read the DMARC verdict off Authentication-Results

MailAuthentication carries a third verdict; the reader distils it the same
way it already distils SPF and DKIM.
EOF
```

---

### Task 2: The partial source fetch

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/MailMessageSource.cs`
- Modify: `src/snoopy.microservice/Services/IImapSession.cs` (after `GetMimeMessageAsync`, the last member)
- Modify: `src/snoopy.microservice/Services/ImapSession.cs` (after `GetMimeMessageAsync`, around line 785)
- Modify: `src/snoopy.microservice/Repositories/IMailMessageRepository.cs` (after `GetMimeMessageAsync`, the last member)
- Modify: `src/snoopy.microservice/Repositories/MailMessageRepository.cs` (after `GetMimeMessageAsync`, the last method)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Models/MailMessageSourceTests.cs` (create; create the `Models` folder if the test project has none)

**Interfaces:**
- Consumes: `MailAuthentication(Spf, Dkim, Dmarc, Raw)` from Task 1.
- Produces:
  - `MailMessageSource` — a record, fields listed in Step 1, plus the static `IsTruncated(long totalBytes, int maxBytes)`.
  - `IImapSession.GetMessageSourceAsync(string folderPath, uint uid, int maxBytes, CancellationToken) → Task<Result<MailMessageSource>>`
  - `IMailMessageRepository.GetSourceAsync(User user, MailAccountConnection connection, string folderPath, uint uid, int maxBytes, CancellationToken) → Task<Result<MailMessageSource>>`

`ImapSession` itself is not unit-tested here — it talks to a live IMAP server, and the repository is a two-line pass-through with no fake in this project. Its behaviour is covered at the controller boundary in Task 3 and by hand at the end of Task 7. The one decision worth pinning in isolation is the truncation boundary, which is why it is a pure static rather than an inline comparison: the `>` versus `>=` mistake is silent, permanent, and invisible at every other layer.

- [ ] **Step 1: Write the failing test**

`snoopy.microservice.Tests/Models/MailMessageSourceTests.cs`:

```csharp
using weesky.Snoopy.Microservice.Models.Mail;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

public sealed class MailMessageSourceTests
{
    private const int Cap = 1024 * 1024;

    [Fact]
    public void IsTruncated_IsFalseBelowTheCap()
        => Assert.False(MailMessageSource.IsTruncated(Cap - 1, Cap));

    /// <summary>A message weighing exactly the cap arrived whole; labelling it truncated
    /// would tell the reader bytes are missing when none are.</summary>
    [Fact]
    public void IsTruncated_IsFalseAtExactlyTheCap()
        => Assert.False(MailMessageSource.IsTruncated(Cap, Cap));

    [Fact]
    public void IsTruncated_IsTrueAboveTheCap()
        => Assert.True(MailMessageSource.IsTruncated(Cap + 1, Cap));
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter MailMessageSourceTests`
Expected: FAIL — `MailMessageSource` does not exist (compile error).

- [ ] **Step 3: Create the model**

`Models/Mail/MailMessageSource.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// A message as it arrived: the headers a reader wants distilled, plus the verbatim RFC822
/// bytes. <paramref name="Source"/> is capped — <paramref name="TotalBytes"/> is what the
/// server reports the whole message weighs, so the client can say what it is not showing.
/// </summary>
public sealed record MailMessageSource(
    string Subject,
    string? MessageId,
    DateTimeOffset Date,
    string FromName,
    string FromAddress,
    IReadOnlyList<MailAddressInfo> To,
    MailAuthentication? Authentication,
    string Source,
    long TotalBytes,
    bool Truncated)
{
    /// <summary>
    /// Truncation is decided from what the server says the message weighs, never from the
    /// length of what came back: a message of exactly the cap arrived whole, and inferring
    /// from the byte count alone would label it truncated forever.
    /// </summary>
    public static bool IsTruncated(long totalBytes, int maxBytes) => totalBytes > maxBytes;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --filter MailMessageSourceTests`
Expected: PASS, 3 tests.

- [ ] **Step 5: Declare it on the session**

`Services/IImapSession.cs`, after the `GetMimeMessageAsync` declaration (the last member of the interface):

```csharp
    /// <summary>
    /// The first <paramref name="maxBytes"/> octets of the message as it arrived, plus the
    /// headers worth distilling. A partial fetch, not a whole download: a 25 MB message is
    /// mostly base64 nobody reads, and the headers sit at the head of the file.
    /// </summary>
    Task<Result<MailMessageSource>> GetMessageSourceAsync(string folderPath, uint uid, int maxBytes, CancellationToken cancellationToken);
```

- [ ] **Step 6: Implement it**

`Services/ImapSession.cs`, straight after `GetMimeMessageAsync` (which ends around line 785). Note it does **not** call `GetMimeMessageAsync`, whose whole point is a complete `BODY[]`:

```csharp
    /// <summary>
    /// One IMAP round trip's worth of work: an envelope-plus-size fetch that also pulls the
    /// Authentication-Results header, then a BODY[]&lt;0.N&gt; partial fetch for the bytes.
    /// </summary>
    public Task<Result<MailMessageSource>> GetMessageSourceAsync(
        string folderPath, uint uid, int maxBytes, CancellationToken cancellationToken) =>
        ExecuteAsync(cancellationToken, async () =>
        {
            var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var uniqueId = new UniqueId(folder.UidValidity, uid);

            var summaries = await folder.FetchAsync(
                new[] { uniqueId },
                MessageSummaryItems.Envelope | MessageSummaryItems.Size,
                new[] { HeaderId.AuthenticationResults },
                cancellationToken);

            var summary = summaries.FirstOrDefault();
            if (summary?.Envelope == null) return Result.Failure<MailMessageSource>(MessageNotFound);

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
            var total = summary.Size.HasValue ? (long)summary.Size.Value : bytes.LongLength;

            return Result.Success(new MailMessageSource(
                Subject: envelope.Subject ?? string.Empty,
                MessageId: TrimAngleBrackets(envelope.MessageId),
                Date: envelope.Date ?? DateTimeOffset.MinValue,
                FromName: from?.Name ?? string.Empty,
                FromAddress: from?.Address ?? string.Empty,
                To: envelope.To.Mailboxes
                    .Select(m => new MailAddressInfo(m.Name ?? string.Empty, m.Address))
                    .ToList(),
                Authentication: MailAuthenticationReader.Parse(summary.Headers),
                Source: source,
                TotalBytes: total,
                Truncated: MailMessageSource.IsTruncated(total, maxBytes)));
        },
            "Unable to read the message source",
            ex => _logger.LogError(ex, "Failed to read the source of {Uid} in {Folder}", uid, folderPath),
            MessageSentinel);
```

`TrimAngleBrackets` is the private helper this file already uses for `ContentId`; reuse it rather than writing a second one. Add `using System.Text;` to the file's usings if it is not already there.

- [ ] **Step 7: Declare it on the repository**

`Repositories/IMailMessageRepository.cs`, after `GetMimeMessageAsync`:

```csharp
    /// <summary>The message as it arrived, capped at <paramref name="maxBytes"/> octets.</summary>
    Task<Result<MailMessageSource>> GetSourceAsync(User user, MailAccountConnection connection, string folderPath, uint uid, int maxBytes, CancellationToken cancellationToken);
```

- [ ] **Step 8: Implement the repository pass-through**

`Repositories/MailMessageRepository.cs`, after `GetMimeMessageAsync`:

```csharp
    public Task<Result<MailMessageSource>> GetSourceAsync(
        User user, MailAccountConnection connection, string folderPath, uint uid, int maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return sessions.WithSessionAsync(connection,
            session => session.GetMessageSourceAsync(folderPath, uid, maxBytes, cancellationToken), cancellationToken);
    }
```

- [ ] **Step 9: Verify it builds and the suite is green**

Run: `dotnet build && dotnet test`
Expected: no errors. Any `IImapSession` or `IMailMessageRepository` test double in the test project that fails to compile needs the new member; implement it as `throw new NotImplementedException()` only if the double is hand-written — a Moq mock needs nothing.

- [ ] **Step 10: Commit**

```bash
git add src/snoopy.microservice
git commit -F - <<'EOF'
Fetch a message's source as a capped byte range

BODY[]<0.N> plus an envelope fetch, so a 25 MB message costs the cap rather
than the whole download.
EOF
```

---

### Task 3: The endpoint

**Files:**
- Modify: `src/snoopy.microservice/Controllers/MailController.cs` (after `GetAttachment`, which ends around line 568)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs` (after the `GetMessage_*` tests, which start around line 483)

**Interfaces:**
- Consumes: `IMailMessageRepository.GetSourceAsync(user, connection, folderPath, uid, maxBytes, ct)` and `MailMessageSource` from Task 2.
- Produces: `GET /api/Mail/Messages/Source?folder=…&uid=…` → 200 `MailMessageSource` · 400 blank folder · 404 no such message · 502 IMAP failure. The controller method is `GetMessageSource(string folder, uint uid, CancellationToken)`.

- [ ] **Step 1: Write the failing tests**

In `MailControllerTests.cs`, after the existing `GetMessage_*` block:

```csharp
    private static MailMessageSource Source(long totalBytes, bool truncated) => new(
        Subject: "Mount ZFS on rescue system",
        MessageId: "c24494a9de@weesky.be",
        Date: new DateTimeOffset(2026, 2, 2, 1, 1, 0, TimeSpan.Zero),
        FromName: "Michaël",
        FromAddress: "darth@weesky.be",
        To: new List<MailAddressInfo> { new("", "darthmaul0181@gmail.com") },
        Authentication: new MailAuthentication("pass", "pass", "pass", "mx.google.com; spf=pass"),
        Source: "Delivered-To: darthmaul0181@gmail.com\r\n",
        TotalBytes: totalBytes,
        Truncated: truncated);

    [Fact]
    public async Task GetMessageSource_ReturnsTheSource()
    {
        _messages.Setup(m => m.GetSourceAsync(
                     It.IsAny<User>(), Conn, "INBOX", 42u, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(Source(1024, false)));

        var result = await CreateController().GetMessageSource("INBOX", 42, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<MailMessageSource>(ok.Value);
        Assert.Equal("Mount ZFS on rescue system", payload.Subject);
        Assert.Equal("pass", payload.Authentication!.Dmarc);
        Assert.False(payload.Truncated);
    }

    [Fact]
    public async Task GetMessageSource_AsksForOneMegabyte()
    {
        _messages.Setup(m => m.GetSourceAsync(
                     It.IsAny<User>(), Conn, "INBOX", 42u, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(Source(1024, false)));

        await CreateController().GetMessageSource("INBOX", 42, CancellationToken.None);

        _messages.Verify(m => m.GetSourceAsync(
            It.IsAny<User>(), Conn, "INBOX", 42u, 1024 * 1024, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMessageSource_Returns400ForABlankFolder()
    {
        var result = await CreateController().GetMessageSource("  ", 42, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetMessageSource_Returns404WhenTheMessageIsGone()
    {
        _messages.Setup(m => m.GetSourceAsync(
                     It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<uint>(),
                     It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<MailMessageSource>(ImapSession.MessageNotFound));

        var result = await CreateController().GetMessageSource("INBOX", 42, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetMessageSource_Returns502WhenImapFails()
    {
        _messages.Setup(m => m.GetSourceAsync(
                     It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<uint>(),
                     It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<MailMessageSource>("Unable to read the message source"));

        var result = await CreateController().GetMessageSource("INBOX", 42, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }
```

`Assert.IsType<BadRequestObjectResult>` and `Assert.IsType<NotFoundObjectResult>` are exact-type assertions, not `ObjectResult` — that distinction is deliberate and matches the rest of this file.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter GetMessageSource`
Expected: FAIL — `MailController` has no `GetMessageSource` (compile error).

- [ ] **Step 3: Add the constant and the endpoint**

`Controllers/MailController.cs`. First the constant, beside the class's other private constants near the top:

```csharp
    /// <summary>
    /// How much of a message the source view may carry. Internal, never a setting: headers sit
    /// at the head of the file, so what a cap drops is the tail of the base64 — and a message
    /// may legitimately weigh MailOptions.MaxMessageSizeMb (25 MB), which no browser wants
    /// dropped into a &lt;pre&gt;.
    /// </summary>
    private const int MaxSourceBytes = 1024 * 1024;
```

Then the action, after `GetAttachment`:

```csharp
    /// <summary>
    /// The message as it arrived: the headers worth distilling plus the verbatim RFC822 bytes,
    /// capped at one megabyte.
    /// </summary>
    /// <param name="folder">full folder path</param>
    /// <param name="uid">message UID, valid only for the folder's current UidValidity</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The source</response>
    /// <response code="400">The folder is missing</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No message with that UID in that folder</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpGet("Messages/Source")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<MailMessageSource>> GetMessageSource(
        [FromQuery] string folder,
        [FromQuery] uint uid,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folder)) return BadRequestEnveloppe("A folder is required");

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

        var result = await messages.GetSourceAsync(
            AuthenticatedUser, connection, folder, uid, MaxSourceBytes, cancellationToken);

        if (result.IsFailure && result.Error == ImapSession.MessageNotFound)
        {
            return NotFoundEnveloppe(result.Error);
        }

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }
```

It does **not** call `RecordSenderUseAsync`: reading a message's source is not reading the message, and touching the trusted-sender entry from here would keep an approval alive on a diagnostic click.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter GetMessageSource`
Expected: PASS, 5 tests.

- [ ] **Step 5: Run the whole backend suite**

Run: `dotnet test`
Expected: PASS. Task 1's record change touched every `new MailAuthentication(...)`; this is where a missed one surfaces.

- [ ] **Step 6: Commit**

```bash
git add src/snoopy.microservice
git commit -F - <<'EOF'
Serve a message's source at GET Messages/Source

One answer carrying the synthesis and the capped RFC822 bytes, so the source
page makes a single request.
EOF
```

---

### Task 4: Link items in DropdownMenu

**Files:**
- Modify: `src/frontend/src/components/DropdownMenu.tsx:3-13` (the types) and `:95-105` (the render)
- Test: `src/frontend/src/components/DropdownMenu.test.tsx` (create if absent)

**Interfaces:**
- Consumes: nothing.
- Produces: `MenuItem` becomes a union — an **action** (`onSelect` required, `disabled`/`title` allowed) or a **link** (`href` required, opens in a new tab, no `disabled`). `MenuEntry = MenuItem | 'separator'` is unchanged, so every existing call site keeps compiling.

- [ ] **Step 1: Write the failing test**

Create `src/frontend/src/components/DropdownMenu.test.tsx` (if the file already exists, append the `describe` body into it):

```tsx
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import DropdownMenu from './DropdownMenu'

function open() {
  fireEvent.click(screen.getByRole('button', { name: 'Menu' }))
}

describe('DropdownMenu', () => {
  it('renders an item carrying href as a link opening in a new tab', () => {
    render(<DropdownMenu ariaLabel="Menu" trigger="⋮"
      items={[{ label: 'View source', href: '/mail/source?folder=INBOX&uid=42' }]} />)
    open()

    const link = screen.getByRole('menuitem', { name: 'View source' })
    expect(link.tagName).toBe('A')
    expect(link).toHaveAttribute('href', '/mail/source?folder=INBOX&uid=42')
    expect(link).toHaveAttribute('target', '_blank')
    // Without it the opened tab gets a handle on window.opener.
    expect(link).toHaveAttribute('rel', expect.stringContaining('noopener'))
  })

  it('closes the menu when a link item is activated', () => {
    render(<DropdownMenu ariaLabel="Menu" trigger="⋮"
      items={[{ label: 'View source', href: '/mail/source' }]} />)
    open()

    fireEvent.click(screen.getByRole('menuitem', { name: 'View source' }))

    expect(screen.queryByRole('menuitem')).not.toBeInTheDocument()
  })

  it('still renders an item carrying onSelect as a button', () => {
    const onSelect = vi.fn()
    render(<DropdownMenu ariaLabel="Menu" trigger="⋮" items={[{ label: 'Archive', onSelect }]} />)
    open()

    const item = screen.getByRole('menuitem', { name: 'Archive' })
    expect(item.tagName).toBe('BUTTON')

    fireEvent.click(item)
    expect(onSelect).toHaveBeenCalledOnce()
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm test -- DropdownMenu`
Expected: FAIL — the link item renders a `<button>` with no `href`, so `link.tagName` is `BUTTON`.

- [ ] **Step 3: Split the item type**

`DropdownMenu.tsx`, replacing lines 3-13:

```tsx
interface MenuItemBase {
  label: string
  /** Rich rendering for the row; `label` stays the key and the accessible-name fallback. */
  node?: ReactNode
  icon?: ReactNode
}

interface MenuAction extends MenuItemBase {
  onSelect: () => void
  disabled?: boolean
  title?: string
  href?: never
}

/** A row that navigates rather than acts. Always a new tab: middle-click, Ctrl+click and the
    browser's own context menu all do nothing on a <button>, and a control that navigates while
    looking like a command teaches the wrong thing about the menu. Never disabled — a greyed
    link has no honest markup, and no caller needs one. */
interface MenuLink extends MenuItemBase {
  href: string
  onSelect?: never
}

export type MenuItem = MenuAction | MenuLink

export type MenuEntry = MenuItem | 'separator'
```

- [ ] **Step 4: Render the link branch**

`DropdownMenu.tsx`, replacing the `<button …role="menuitem">` block (lines 99-104) with:

```tsx
            ) : entry.href !== undefined ? (
              <a key={entry.label} role="menuitem" className="dropdown-item" href={entry.href}
                target="_blank" rel="noopener" onClick={() => setOpen(false)}>
                {entry.icon}
                {entry.node ?? entry.label}
              </a>
            ) : (
              <button key={entry.label} type="button" role="menuitem" className="dropdown-item"
                disabled={entry.disabled} title={entry.title}
                onClick={() => { setOpen(false); entry.onSelect() }}>
                {entry.icon}
                {entry.node ?? entry.label}
              </button>
            )
```

- [ ] **Step 5: Give the anchor the button's skin**

`src/frontend/src/styles/shell.css`, directly after the `.dropdown-item:hover` rule at line 262. An `<a>` does not inherit `font`, and it arrives underlined and link-coloured:

```css
a.dropdown-item { text-decoration: none; color: inherit; font: inherit; }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `npm test -- DropdownMenu`
Expected: PASS, 3 tests.

- [ ] **Step 7: Typecheck the existing call sites**

Run: `npm run typecheck`
Expected: no errors. `ReaderActions`, `SelectionToolbar`, `IdentityMenu`, `MessageReader` and the attachment chip all build `MenuAction`s, which the union still accepts unchanged.

- [ ] **Step 8: Commit**

```bash
git add src/frontend/src/components src/frontend/src/styles
git commit -F - <<'EOF'
Let a dropdown item be a link

An item carrying href renders as an anchor opening a new tab, so middle-click
and Ctrl+click work where a button swallowed them.
EOF
```

---

### Task 5: The client data layer

**Files:**
- Modify: `src/frontend/src/modules/mail/api/mailTypes.ts:79-84` (the `MailAuthentication` interface) and the end of the file
- Modify: `src/frontend/src/api.js:290-291` (beside `getMailMessage`)
- Modify: `src/frontend/src/modules/mail/queries.ts:44-45` (`mailKeys`) and `:142-150` (beside `useMessage`)

**Interfaces:**
- Consumes: the `GET /api/Mail/Messages/Source` contract from Task 3.
- Produces:
  - `MailAuthentication` gains `dmarc: string | null`.
  - `MailMessageSource` (TS interface) — fields as in Step 1.
  - `api.getMessageSource(folder, uid, options)` → `Promise<MailMessageSource>`
  - `mailKeys.messageSource(accountId, folder, uid)` → `['mail', accountId, 'source', folder, uid]`
  - `useMessageSource(folderPath: string | null, uid: number | null)` → `UseQueryResult<MailMessageSource>`

- [ ] **Step 1: Extend and add the types**

`mailTypes.ts`, replacing the `MailAuthentication` interface:

```ts
/** SPF/DKIM/DMARC as the receiving server reported them, plus the raw header behind them. */
export interface MailAuthentication {
  spf: string | null
  dkim: string | null
  dmarc: string | null
  raw: string
}
```

And at the end of the file:

```ts
/** A message as it arrived. `source` is capped; `totalBytes` is what the whole message weighs. */
export interface MailMessageSource {
  subject: string
  messageId: string | null
  date: string
  fromName: string
  fromAddress: string
  to: MailAddressInfo[]
  authentication: MailAuthentication | null
  source: string
  totalBytes: number
  truncated: boolean
}
```

- [ ] **Step 2: Add the API method**

`api.js`, directly under `getMailMessage`:

```js
  getMessageSource: (folder, uid, options) =>
    request('GET', `/api/Mail/Messages/Source?folder=${encodeURIComponent(folder)}&uid=${uid}`, undefined, options),
```

`encodeURIComponent` on the folder is not optional — a path may contain `/`, `&` or `#`.

- [ ] **Step 3: Add the key and the hook**

`queries.ts`, in `mailKeys` directly under `message`:

```ts
  messageSource: (accountId: string, folder: string, uid: number) =>
    ['mail', accountId, 'source', folder, uid] as const,
```

And beside `useMessage`:

```ts
/** The message as it arrived. Its own key rather than a slice of `message`: what it caches is
    the RFC822 bytes, and a reader that has the detail has not paid for those. */
export function useMessageSource(folderPath: string | null, uid: number | null) {
  const accountId = useAccountId()

  return useQuery<MailMessageSource>({
    queryKey: mailKeys.messageSource(accountId, folderPath ?? '', uid ?? 0),
    queryFn: ({ signal }) => api.getMessageSource(folderPath, uid, { signal, accountId }),
    enabled: folderPath !== null && uid !== null,
  })
}
```

Add `MailMessageSource` to the existing `import type { … } from './api/mailTypes'` line at the top of `queries.ts`.

- [ ] **Step 4: Typecheck**

Run: `npm run typecheck`
Expected: no errors.

- [ ] **Step 5: Run the suite**

Run: `npm test`
Expected: PASS. Any test fixture building a `MailAuthentication` object literal now needs `dmarc`; add `dmarc: null` (or the real verdict where the case is about auth) to each one the compiler names.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src
git commit -F - <<'EOF'
Wire the message-source endpoint into the client

Type, api method and useMessageSource on a key of its own; MailAuthentication
gains the dmarc verdict.
EOF
```

---

### Task 6: The source page and its route

**Files:**
- Create: `src/frontend/src/icons/CodeIcon.tsx`
- Create: `src/frontend/src/modules/mail/source/MessageSourceView.tsx`
- Create: `src/frontend/src/modules/mail/source/MessageSourceView.test.tsx`
- Modify: `src/frontend/src/icons/icons.test.tsx` (import and list the new icon)
- Modify: `src/frontend/src/routes.tsx:17-22` (the lazy imports) and `:26-30` (under `RequireAuth`, beside `AppShell`)
- Modify: `src/frontend/src/styles/mail.css` (append at the end)

**Interfaces:**
- Consumes: `useMessageSource` and `MailMessageSource` from Task 5; `LoadingBlock` from `src/components/LoadingBlock`; `formatReaderDate` from `../reader/formatReaderDate`; `formatSize` from `../reader/formatSize` (both named exports).
- Produces: the route `/mail/source`, and `CodeIcon` for Task 7.

- [ ] **Step 1: Write the failing test**

`src/frontend/src/modules/mail/source/MessageSourceView.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import MessageSourceView from './MessageSourceView'

const useMessageSource = vi.fn()
vi.mock('../queries', () => ({ useMessageSource: (...a: unknown[]) => useMessageSource(...a) }))

const payload = {
  subject: 'Mount ZFS on rescue system',
  messageId: 'c24494a9de@weesky.be',
  date: '2026-02-02T01:01:00Z',
  fromName: 'Michaël',
  fromAddress: 'darth@weesky.be',
  to: [{ name: '', address: 'darthmaul0181@gmail.com' }],
  authentication: { spf: 'pass', dkim: 'pass', dmarc: 'pass', raw: 'mx.google.com; spf=pass' },
  source: 'Delivered-To: darthmaul0181@gmail.com\r\nSubject: Mount ZFS\r\n',
  totalBytes: 2048,
  truncated: false,
}

function renderAt(search: string) {
  return render(
    <MemoryRouter initialEntries={[`/mail/source${search}`]}>
      <MessageSourceView />
    </MemoryRouter>,
  )
}

describe('MessageSourceView', () => {
  beforeEach(() => {
    useMessageSource.mockReset()
    useMessageSource.mockReturnValue({ data: payload, isLoading: false, error: null, refetch: vi.fn() })
  })

  it('shows the synthesis and the raw source', () => {
    renderAt('?folder=INBOX&uid=42')

    expect(screen.getByText('c24494a9de@weesky.be')).toBeInTheDocument()
    expect(screen.getByText('darthmaul0181@gmail.com')).toBeInTheDocument()
    expect(screen.getByText('pass · pass · pass')).toBeInTheDocument()
    expect(screen.getByText(/Delivered-To: darthmaul0181@gmail\.com/)).toBeInTheDocument()
  })

  it('omits a row whose datum is missing', () => {
    useMessageSource.mockReturnValue({
      data: { ...payload, messageId: null, authentication: null },
      isLoading: false, error: null, refetch: vi.fn(),
    })
    renderAt('?folder=INBOX&uid=42')

    expect(screen.queryByText('Message ID')).not.toBeInTheDocument()
    expect(screen.queryByText('SPF / DKIM / DMARC')).not.toBeInTheDocument()
  })

  it('says what it is not showing when the source is truncated', () => {
    useMessageSource.mockReturnValue({
      data: { ...payload, truncated: true, totalBytes: 25_480_000 },
      isLoading: false, error: null, refetch: vi.fn(),
    })
    renderAt('?folder=INBOX&uid=42')

    expect(screen.getByText('— truncated at 1 MB of 24.3 MB —')).toBeInTheDocument()
  })

  it('carries no truncation marker on a whole source', () => {
    renderAt('?folder=INBOX&uid=42')

    expect(screen.queryByText(/truncated at/)).not.toBeInTheDocument()
  })

  it('titles the tab with the subject', () => {
    renderAt('?folder=INBOX&uid=42')

    expect(document.title).toBe('Mount ZFS on rescue system — source')
  })

  it('offers a retry when the read fails', () => {
    useMessageSource.mockReturnValue({
      data: undefined, isLoading: false, error: new Error('nope'), refetch: vi.fn(),
    })
    renderAt('?folder=INBOX&uid=42')

    expect(screen.getByText('Could not load the message source')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  it('refuses a URL naming no message, without requesting anything', () => {
    renderAt('?folder=INBOX')

    expect(screen.getByText('Could not load the message source')).toBeInTheDocument()
    expect(useMessageSource).toHaveBeenCalledWith(null, null)
  })

  it('offers no retry on a URL naming no message', () => {
    renderAt('?folder=INBOX')

    // The query never ran, so refetch() would do nothing: a button that cannot act is worse
    // than no button.
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm test -- MessageSourceView`
Expected: FAIL — cannot resolve `./MessageSourceView`.

- [ ] **Step 3: Write the icon**

`src/frontend/src/icons/CodeIcon.tsx`:

```tsx
export default function CodeIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke="currentColor"
      strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M5.5 4.5 2 8l3.5 3.5M10.5 4.5 14 8l-3.5 3.5" />
    </svg>
  )
}
```

Then add it to `src/icons/icons.test.tsx`: an `import CodeIcon from './CodeIcon'` beside the others, and this entry at the end of the `icons` array (which starts at line 33):

```tsx
  { name: 'CodeIcon', Icon: CodeIcon, defaultSize: '16' },
```

That file's `it.each` then covers the new icon with the whole set's assertions — default size, size override, and `stroke="currentColor"`.

- [ ] **Step 4: Write the view**

`src/frontend/src/modules/mail/source/MessageSourceView.tsx`:

```tsx
import { useEffect } from 'react'
import { useSearchParams } from 'react-router-dom'
import LoadingBlock from '../../../components/LoadingBlock'
import { formatReaderDate } from '../reader/formatReaderDate'
import { formatSize } from '../reader/formatSize'
import { useMessageSource } from '../queries'

/**
 * The message as it arrived, on its own tab. Deliberately chrome-less — no rail, no folder
 * tree, no way back: the route is a sibling of AppShell rather than a child, which is what
 * keeps useFolders' 60-second poll out of a tab whose only job is to show a text file.
 */
export default function MessageSourceView() {
  const [params] = useSearchParams()
  const folder = params.get('folder')
  const rawUid = params.get('uid')
  const uid = rawUid !== null && /^\d+$/.test(rawUid) ? Number(rawUid) : null
  // A malformed URL is a hand-edited one: it asks for nothing rather than for message 0.
  const addressed = folder !== null && folder !== '' && uid !== null

  const { data, isLoading, error, refetch } = useMessageSource(
    addressed ? folder : null, addressed ? uid : null)

  useEffect(() => {
    if (data) document.title = `${data.subject || '(no subject)'} — source`
  }, [data])

  // One line for both, but the Retry only where there is something to retry: the query is
  // disabled on a malformed URL, so refetch() there would be a button that cannot act.
  if (!addressed || error) {
    return (
      <div className="source-page">
        <p className="source-error">Could not load the message source</p>
        {addressed && (
          <button type="button" className="btn" onClick={() => void refetch()}>Retry</button>
        )}
      </div>
    )
  }

  if (isLoading || !data) return <div className="source-page"><LoadingBlock /></div>

  const verdicts = data.authentication
    && [data.authentication.spf, data.authentication.dkim, data.authentication.dmarc]
      .map(v => v ?? '—').join(' · ')

  return (
    <div className="source-page">
      <h1 className="source-title">Original message</h1>
      <dl className="source-summary">
        {data.messageId && <><dt>Message ID</dt><dd>{data.messageId}</dd></>}
        <dt>Created at</dt><dd>{formatReaderDate(data.date)}</dd>
        <dt>From</dt>
        <dd>{data.fromName ? `${data.fromName} <${data.fromAddress}>` : data.fromAddress}</dd>
        {data.to.length > 0 && (
          <><dt>To</dt><dd>{data.to.map(a => a.address).join(', ')}</dd></>
        )}
        {data.subject && <><dt>Subject</dt><dd>{data.subject}</dd></>}
        {verdicts && <><dt>SPF / DKIM / DMARC</dt><dd>{verdicts}</dd></>}
      </dl>
      {/* Text, rendered as text. No dangerouslySetInnerHTML here, ever: what makes the message
          body need an iframe and two sanitising passes is that the browser parses it. */}
      <pre className="source-raw">{data.source}</pre>
      {data.truncated && (
        <p className="source-truncated">
          — truncated at 1 MB of {formatSize(data.totalBytes)} —
        </p>
      )}
    </div>
  )
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `npm test -- MessageSourceView`
Expected: PASS, 8 tests.

- [ ] **Step 6: Add the route**

`routes.tsx`. Beside the other lazy imports:

```tsx
const MessageSourceView = lazy(() => import('./modules/mail/source/MessageSourceView'))
```

Then, inside the `RequireAuth` children array, **as a sibling of the `AppShell` entry** — not inside its `children`:

```tsx
      // A sibling of AppShell, not a child: that placement is what leaves the rail and the
      // folder column out, and with them useFolders' poll, in a tab that only shows a text file.
      { path: 'mail/source', element: <Suspense fallback={null}><MessageSourceView /></Suspense> },
```

Place it **before** the `{ element: <AppShell />, … }` entry for readability; React Router ranks by specificity, so the shell's `{ path: '*' }` catch-all cannot steal it either way.

- [ ] **Step 7: Style the page**

Append to `src/frontend/src/styles/mail.css`:

```css
/* The source page: a route of its own, so it owns the viewport rather than a column. */
.source-page {
  height: 100vh;
  overflow-y: auto;
  padding: 24px 28px;
  background: var(--bg);
  color: var(--text);
}

.source-title { margin: 0 0 16px; font-size: 1.15rem; }

.source-summary {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 6px 18px;
  margin: 0 0 20px;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  padding: 14px 18px;
  background: var(--surface);
}

.source-summary dt { color: var(--text-muted); }
.source-summary dd { margin: 0; overflow-wrap: break-word; min-width: 0; }

/* `pre`, not `pre-wrap`: a Received: line stays on one line, and the block scrolls on its own
   rather than making the page scroll sideways. This is the whole argument for the full-width
   page over a modal. */
.source-raw {
  margin: 0;
  padding: 14px 16px;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface-sunken);
  color: var(--text-muted);
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 0.78rem;
  line-height: 1.6;
  white-space: pre;
  overflow-x: auto;
}

.source-error { color: var(--text-muted); margin: 0 0 12px; }
.source-truncated { margin: 10px 0 0; color: var(--text-muted); font-size: 0.8rem; }
```

- [ ] **Step 8: Verify it in a browser**

Run: `npm run dev`, open a message, and hit `/mail/source?folder=INBOX&uid=<a real uid>` directly.
Check: no rail and no folder column; a long `Received:` line stays on one line and the `<pre>` scrolls horizontally while the page does not; the theme applies (`ThemeProvider` sits above the router, so it does). **jsdom sees no layout — none of this is assertable in a test.**

- [ ] **Step 9: Run the full suite, lint and typecheck**

Run: `npm test && npm run lint && npm run typecheck`
Expected: all PASS.

- [ ] **Step 10: Commit**

```bash
git add src/frontend/src
git commit -F - <<'EOF'
Add the chrome-less message source page

A route sibling of AppShell so the tab carries no rail, no folder tree and no
folder poll — just the synthesis and the raw bytes.
EOF
```

---

### Task 7: The View source entry

**Files:**
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx:233-258` (the `actions` array)
- Test: `src/frontend/src/modules/mail/reader/MessageReader.test.tsx`

**Interfaces:**
- Consumes: `MenuLink` (the `href` variant) from Task 4, `CodeIcon` from Task 6, the `/mail/source` route from Task 6.
- Produces: nothing further.

The test goes in `MessageReader.test.tsx`, **not** in `ReaderActions.test.tsx`. `ReaderActions` takes `actions` as a prop, so a test there would only re-assert Task 4's rendering under a different name. What is new here is the reader *building* the entry with the right href, and `MessageReader` is the only component that does that.

- [ ] **Step 1: Write the failing test**

`MessageReader.test.tsx` already has the harness this needs — the hoisted `mocks`, the `wrapper`, and a dozen tests opening the kebab the same way. Add, beside the other kebab tests (around line 517):

```tsx
  it('offers View source as a link to the message on its own tab', async () => {
    mocks.getMailMessage.mockResolvedValue(blocked)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')
    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

    const link = screen.getByRole('menuitem', { name: 'View source' })
    expect(link).toHaveAttribute('href', '/mail/source?folder=INBOX&uid=2')
    expect(link).toHaveAttribute('target', '_blank')
  })

  it('percent-encodes a folder path carrying the hierarchy separator', async () => {
    mocks.getMailMessage.mockResolvedValue(blocked)

    render(<MessageReader folderPath="INBOX/Ops & Co" uid={7} />, { wrapper })
    await screen.findByText('Re: facture')
    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

    expect(screen.getByRole('menuitem', { name: 'View source' }))
      .toHaveAttribute('href', '/mail/source?folder=INBOX%2FOps%20%26%20Co&uid=7')
  })
```

`blocked` is the fixture the surrounding tests already use; if the two tests are placed in a different `describe` block that does not have it in scope, use whichever message fixture that block uses and adjust the `findByText` subject to match.

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm test -- MessageReader`
Expected: FAIL — `Unable to find an accessible element with the role "menuitem" and name "View source"`.

- [ ] **Step 3: Add the entry in MessageReader**

`MessageReader.tsx`, after the `if (senderApproved && …)` block that pushes the image-blocking entry (so View source is always last, whatever else the message earned):

```tsx
  // Its own group: this is neither a flag nor a move but a look at the bytes. A link rather
  // than a button so middle-click and Ctrl+click open the tab the entry promises.
  actions.push('separator', {
    label: 'View source',
    icon: <CodeIcon size={18} />,
    href: `/mail/source?folder=${encodeURIComponent(folderPath!)}&uid=${uid}`,
  })
```

Add `import CodeIcon from '../../../icons/CodeIcon'` beside the file's other icon imports.

`folderPath` and `uid` are the same non-null values the surrounding `onToggleSeen`/`onToggleFlagged` handlers already assert with `!` at this point in the component — the reader does not render without a message.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `npm test -- MessageReader ReaderActions`
Expected: PASS. If an existing assertion counting the menu's entries now fails, update it — View source is a genuine new entry, not a regression.

- [ ] **Step 5: Verify the whole path in a browser**

Run: `npm run dev`. Open a message, open the kebab, and check:
- "View source" sits at the foot under its own rule.
- A plain click opens a new tab on the chrome-less page showing that message's source.
- A middle-click and a Ctrl+click do the same without closing the reader.
- A message carrying an attachment shows the truncation line if it weighs over 1 MB.

- [ ] **Step 6: Run everything**

Run: `npm test && npm run lint && npm run typecheck && npm run build`
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src
git commit -F - <<'EOF'
Offer View source at the foot of the reader's kebab

Its own group under a rule, as a link so middle-click and Ctrl+click open the
tab the entry promises.
EOF
```

---

## Manual verification checklist

Nothing below is assertable in jsdom; all of it is a browser check at the end of Task 7.

- [ ] A 30 MB message with attachments: the tab opens fast, the source stops on the truncation line naming the real total.
- [ ] A message from a connected (non-primary) account: the new tab shows **that** account's message, not the primary's — the `X-Account-Id` header is seeded from `localStorage`, so this is the case that would break if it were not.
- [ ] A message whose `Authentication-Results` carries no DMARC: the row shows `pass · pass · —` rather than disappearing.
- [ ] A message with no `Authentication-Results` at all: the row is absent entirely.
- [ ] Reloading the source URL: the page comes back rather than redirecting.
- [ ] Light theme and dark theme both legible.
