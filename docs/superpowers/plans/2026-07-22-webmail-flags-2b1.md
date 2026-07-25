# Tranche 2b1 — Drapeaux (lu/non-lu, suivi) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** First IMAP writes: set/clear `\Seen` and `\Flagged` on messages — batch endpoint, optimistic caches, star + hover cluster on list rows, first two kebab menu entries, mark-read-on-open.

**Architecture:** One batch endpoint `PUT /api/Mail/Messages/Flags` flows Controller → `IMailMessageRepository` → `IImapSession` (folder opened ReadWrite, `AddFlags`/`RemoveFlags` silent). The frontend mutates optimistically across three caches (paged lists, stream blocks, folder tree unread counts) with snapshot rollback — never `invalidateQueries` on the stream. Mark-read fires client-side once per message open.

**Tech Stack:** ASP.NET Core (.NET 10) + MailKit + xUnit/Moq; React + TanStack Query + Vitest/Testing Library.

**Spec:** `docs/superpowers/specs/2026-07-22-webmail-flags-2b1-design.md`

## Global Constraints

- Commit messages: concise, **two lines max** (repo rule).
- Code comments only where the code can't say it; **3 lines max** (repo rule).
- UI copy is **English**; conversation with the user is French.
- Frontend colours: **tokens only**, never literals. Star uses `var(--badge-count-bg)` (amber).
- **Never call `invalidateQueries` on the messageStream key** — replaying N blocks costs N IMAP connections.
- Backend: `dotnet test` (never `--no-build`) whenever new test files were added.
- Backend style: file-scoped namespaces, one type per file (grouped small request DTOs tolerated, cf. `FolderRequests.cs`), `Async` suffix, cancellation tokens everywhere, `Result`/`Result<T>` for failures.
- Folder paths travel in the **body or query string**, never in a route segment.
- Frontend tests sit next to what they test; backend tests in `snoopy.microservice.Tests`.
- Working directory for backend commands: `src/snoopy.microservice`; frontend: `src/frontend`.

**Note on spec §6:** the spec names `ImapSessionTests.SetFlagsAsync`. The established seam is different: `ImapSession`'s MailKit-touching methods have no unit tests (concrete `ImapClient`, not mockable) — only its pure helpers are tested. The delegation is tested at `MailMessageRepositoryTests` (Moq `IImapSession`) and `MailControllerTests` (Moq `IMailMessageRepository`), exactly like every existing message method. This plan follows the established seam.

---

### Task 1: Backend — models, session, repository

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/MailFlag.cs`
- Create: `src/snoopy.microservice/Models/Mail/MessageRequests.cs`
- Modify: `src/snoopy.microservice/Services/IImapSession.cs`
- Modify: `src/snoopy.microservice/Services/ImapSession.cs` (after `SetSubscriptionAsync`, ~line 225)
- Modify: `src/snoopy.microservice/Repositories/IMailMessageRepository.cs`
- Modify: `src/snoopy.microservice/Repositories/MailMessageRepository.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/MailMessageRepositoryTests.cs`

**Interfaces:**
- Consumes: existing `IImapConnectionFactory.OpenAsync`, `Result` from CSharpFunctionalExtensions.
- Produces: `MailFlag { Seen, Flagged }`; `SetMessageFlagsRequest { FolderPath, Uids, Flag, Value }`;
  `Task<Result> IImapSession.SetFlagsAsync(string folderPath, IReadOnlyList<uint> uids, MailFlag flag, bool value, CancellationToken ct)`;
  `Task<Result> IMailMessageRepository.SetFlagsAsync(User user, string password, string folderPath, IReadOnlyList<uint> uids, MailFlag flag, bool value, CancellationToken ct)` — Task 2 calls the repository method.

- [ ] **Step 1: Write the failing repository tests**

Append to `MailMessageRepositoryTests.cs`, following the file's existing `CreateSut()` helper (mocks `IImapConnectionFactory` + `IImapSession`; mirror `ListAsync_DelegatesToTheSessionWithTheRequestedPage` for shapes):

```csharp
[Fact]
public async Task SetFlagsAsync_DelegatesToTheSession()
{
    var (repo, _, session) = CreateSut();
    session.Setup(s => s.SetFlagsAsync("INBOX", It.IsAny<IReadOnlyList<uint>>(), MailFlag.Seen, true, It.IsAny<CancellationToken>()))
           .ReturnsAsync(Result.Success());

    var result = await repo.SetFlagsAsync(User(), "pw", "INBOX", [1u, 2u], MailFlag.Seen, true, CancellationToken.None);

    Assert.True(result.IsSuccess);
    session.Verify(s => s.SetFlagsAsync("INBOX",
        It.Is<IReadOnlyList<uint>>(u => u.SequenceEqual(new uint[] { 1, 2 })),
        MailFlag.Seen, true, It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task SetFlagsAsync_PropagatesAConnectionFailure()
{
    var (repo, factory, _) = CreateSut();
    factory.Setup(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(Result.Failure<IImapSession>("down"));

    var result = await repo.SetFlagsAsync(User(), "pw", "INBOX", [1u], MailFlag.Flagged, false, CancellationToken.None);

    Assert.True(result.IsFailure);
    Assert.Equal("down", result.Error);
}

[Fact]
public async Task SetFlagsAsync_DisposesTheSession()
{
    var (repo, _, session) = CreateSut();
    session.Setup(s => s.SetFlagsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<uint>>(), It.IsAny<MailFlag>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(Result.Success());

    await repo.SetFlagsAsync(User(), "pw", "INBOX", [1u], MailFlag.Seen, true, CancellationToken.None);

    session.Verify(s => s.DisposeAsync(), Times.Once);
}

[Fact]
public async Task SetFlagsAsync_ThrowsWhenUserIsNull()
{
    var (repo, _, _) = CreateSut();

    await Assert.ThrowsAsync<ArgumentNullException>(
        () => repo.SetFlagsAsync(null!, "pw", "INBOX", [1u], MailFlag.Seen, true, CancellationToken.None));
}
```

If the file's user builder is named differently than `User()`, reuse whatever `ListAsync_ThrowsWhenUserIsNull` uses.

- [ ] **Step 2: Run to verify failure**

Run (in `src/snoopy.microservice`): `dotnet test --filter "FullyQualifiedName~MailMessageRepositoryTests"`
Expected: compile errors — `MailFlag` and `SetFlagsAsync` don't exist.

- [ ] **Step 3: Implement**

`Models/Mail/MailFlag.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>A message flag the client may set or clear. Serialised as a string in JSON.</summary>
public enum MailFlag
{
    Seen,
    Flagged
}
```

`Models/Mail/MessageRequests.cs` (grouped like `FolderRequests.cs`):

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// Batch from day one — multi-select (2b3) reuses this unchanged. The folder path travels
/// in the body, never in a route segment: the hierarchy separator may be '/'.
/// </summary>
public sealed class SetMessageFlagsRequest
{
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>1 to 200 entries — the same ceiling as pageSize.</summary>
    public IReadOnlyList<uint> Uids { get; set; } = [];

    public MailFlag Flag { get; set; }

    /// <summary>True sets the flag, false clears it.</summary>
    public bool Value { get; set; }
}
```

`IImapSession.cs` — add to the interface:

```csharp
Task<Result> SetFlagsAsync(string folderPath, IReadOnlyList<uint> uids, MailFlag flag, bool value, CancellationToken cancellationToken);
```

`ImapSession.cs` — after `SetSubscriptionAsync` (same try/catch shape; `FolderNotFound` is the existing constant used by `GetFolderStatusAsync`):

```csharp
public async Task<Result> SetFlagsAsync(string folderPath, IReadOnlyList<uint> uids, MailFlag flag, bool value, CancellationToken cancellationToken)
{
    ThrowIfDisposed();

    try
    {
        var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
        // First ReadWrite open of the project: every read path stays ReadOnly.
        await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

        var messageFlags = flag == MailFlag.Seen ? MessageFlags.Seen : MessageFlags.Flagged;
        var ids = uids.Select(uid => new UniqueId(uid)).ToList();

        // A UID that no longer exists is a silent server-side no-op: the batch never fails partially.
        if (value) await folder.AddFlagsAsync(ids, messageFlags, silent: true, cancellationToken);
        else await folder.RemoveFlagsAsync(ids, messageFlags, silent: true, cancellationToken);

        return Result.Success();
    }
    catch (FolderNotFoundException)
    {
        return Result.Failure(FolderNotFound);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to set {Flag}={Value} on {Count} messages in {Folder}", flag, value, uids.Count, folderPath);
        return Result.Failure("Unable to update the messages");
    }
}
```

`IMailMessageRepository.cs` — add:

```csharp
Task<Result> SetFlagsAsync(User user, string password, string folderPath, IReadOnlyList<uint> uids, MailFlag flag, bool value, CancellationToken cancellationToken);
```

`MailMessageRepository.cs` — same shape as its three siblings:

```csharp
public async Task<Result> SetFlagsAsync(User user, string password, string folderPath, IReadOnlyList<uint> uids, MailFlag flag, bool value, CancellationToken cancellationToken)
{
    if (user == null) throw new ArgumentNullException(nameof(user));

    var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
    if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
    await using var session = sessionResult.Value;

    return await session.SetFlagsAsync(folderPath, uids, flag, value, cancellationToken);
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --filter "FullyQualifiedName~MailMessageRepositoryTests"`
Expected: PASS (all, including the four new ones).

- [ ] **Step 5: Commit**

```bash
git add -A src/snoopy.microservice
git commit -m "Add SetFlagsAsync through session and repository, ReadWrite open"
```

---

### Task 2: Backend — controller action

**Files:**
- Modify: `src/snoopy.microservice/Controllers/MailController.cs` (after `GetAttachment`, ~line 477)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs`

**Interfaces:**
- Consumes: `IMailMessageRepository.SetFlagsAsync` (Task 1), `SetMessageFlagsRequest`, `FromResult` non-generic overload with `successStatusCode` (used by `SetFolderSubscription`).
- Produces: `PUT /api/Mail/Messages/Flags` → 204/400/401/502. Task 3's `api.js` method calls it with JSON body `{ folderPath, uids, flag, value }` (`flag` as string — `JsonStringEnumConverter` is global in `Program.cs` and case-insensitive on read).

- [ ] **Step 1: Write the failing controller tests**

Append to `MailControllerTests.cs`, using the file's `CreateController()`, `_messages` and `_credentials` mocks (mirror the `SetFolderSubscription` group at ~line 244):

```csharp
[Fact]
public async Task SetMessageFlags_Returns204AndDelegates()
{
    _messages.Setup(m => m.SetFlagsAsync(It.IsAny<User>(), "hunter2", "INBOX",
            It.IsAny<IReadOnlyList<uint>>(), MailFlag.Seen, true, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Success());

    var result = await CreateController().SetMessageFlags(
        new SetMessageFlagsRequest { FolderPath = "INBOX", Uids = [42u], Flag = MailFlag.Seen, Value = true },
        CancellationToken.None);

    Assert.IsType<NoContentResult>(result);
    _messages.Verify(m => m.SetFlagsAsync(It.IsAny<User>(), "hunter2", "INBOX",
        It.Is<IReadOnlyList<uint>>(u => u.SequenceEqual(new uint[] { 42 })),
        MailFlag.Seen, true, It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task SetMessageFlags_Returns400WithoutAFolder()
{
    var result = await CreateController().SetMessageFlags(
        new SetMessageFlagsRequest { FolderPath = " ", Uids = [1u], Flag = MailFlag.Seen, Value = true },
        CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result);
}

[Fact]
public async Task SetMessageFlags_Returns400OnAnEmptyBatch()
{
    var result = await CreateController().SetMessageFlags(
        new SetMessageFlagsRequest { FolderPath = "INBOX", Uids = [], Flag = MailFlag.Seen, Value = true },
        CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result);
}

[Fact]
public async Task SetMessageFlags_Returns400Above200Uids()
{
    var uids = Enumerable.Range(1, 201).Select(i => (uint)i).ToList();

    var result = await CreateController().SetMessageFlags(
        new SetMessageFlagsRequest { FolderPath = "INBOX", Uids = uids, Flag = MailFlag.Flagged, Value = true },
        CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result);
}

[Fact]
public async Task SetMessageFlags_Returns401WhenCredentialsAreUnavailable()
{
    var controller = CreateController();
    _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                .Returns(Result.Failure<string>("credentials_unavailable"));

    var result = await controller.SetMessageFlags(
        new SetMessageFlagsRequest { FolderPath = "INBOX", Uids = [1u], Flag = MailFlag.Seen, Value = true },
        CancellationToken.None);

    Assert.IsType<UnauthorizedObjectResult>(result);
}

[Fact]
public async Task SetMessageFlags_Returns502WhenTheServerRefuses()
{
    _messages.Setup(m => m.SetFlagsAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<uint>>(), It.IsAny<MailFlag>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Failure("Unable to update the messages"));

    var result = await CreateController().SetMessageFlags(
        new SetMessageFlagsRequest { FolderPath = "INBOX", Uids = [1u], Flag = MailFlag.Seen, Value = true },
        CancellationToken.None);

    var status = Assert.IsType<ObjectResult>(result);
    Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter "FullyQualifiedName~MailControllerTests.SetMessageFlags"`
Expected: compile error — `SetMessageFlags` doesn't exist on the controller.

- [ ] **Step 3: Implement the action**

In `MailController.cs`, after `GetAttachment`:

```csharp
/// <summary>
/// Sets or clears one flag on a batch of messages. A UID that no longer exists is a
/// silent no-op: the batch never fails partially.
/// </summary>
/// <param name="request">folder, UIDs, the flag and the value to write</param>
/// <param name="cancellationToken">cancellation token</param>
/// <response code="204">The flags were written</response>
/// <response code="400">The folder is missing, or the batch is empty or above 200 UIDs</response>
/// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
/// <response code="502">The mail server could not be reached</response>
[HttpPut("Messages/Flags")]
[ProducesResponseType(StatusCodes.Status204NoContent)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status502BadGateway)]
public async Task<ActionResult> SetMessageFlags(SetMessageFlagsRequest request, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));
    if (request.Uids.Count is < 1 or > 200) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Uids must hold between 1 and 200 entries"));

    var password = _credentials.Retrieve(Request);
    if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

    var result = await _messages.SetFlagsAsync(
        AuthenticatedUser, password.Value, request.FolderPath, request.Uids, request.Flag, request.Value, cancellationToken);

    return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway, successStatusCode: StatusCodes.Status204NoContent);
}
```

If `FromResult`'s success path returns something other than `NoContentResult` here, mirror exactly what `SetFolderSubscription` does — the two actions must stay twins.

- [ ] **Step 4: Run the full backend suite**

Run: `dotnet test`
Expected: PASS. (`ApiDocumentation.xml` regenerates during build — commit it too.)

- [ ] **Step 5: Commit**

```bash
git add -A src/snoopy.microservice
git commit -m "PUT Messages/Flags: batch seen/flagged endpoint, 204 on success"
```

---

### Task 3: Frontend — API client method

**Files:**
- Modify: `src/frontend/src/api.js` (mail section, after `getMailMessage` ~line 227)
- Test: `src/frontend/src/api.test.js`

**Interfaces:**
- Produces: `api.setMessageFlags(folder, uids, flag, value)` — `flag` is the string `'seen' | 'flagged'` (the backend's `JsonStringEnumConverter` reads case-insensitively). Tasks 5+ call it.

- [ ] **Step 1: Write the failing test**

In `api.test.js`, next to the other mail-method tests, following the file's existing fetch-mock pattern:

```js
it('setMessageFlags PUTs the batch body', async () => {
  fetch.mockResolvedValueOnce(okResponse({}))

  await api.setMessageFlags('INBOX/Sub', [1, 2], 'seen', true)

  const [url, options] = fetch.mock.calls[0]
  expect(url).toBe('https://api.mail.weesky.net/api/Mail/Messages/Flags')
  expect(options.method).toBe('PUT')
  expect(JSON.parse(options.body)).toEqual({ folderPath: 'INBOX/Sub', uids: [1, 2], flag: 'seen', value: true })
})
```

Reuse the file's actual helper for a 200 response (`okResponse` above is a stand-in — copy whatever the neighbouring mail tests use).

- [ ] **Step 2: Run to verify failure**

Run (in `src/frontend`): `npx vitest run src/api.test.js`
Expected: FAIL — `api.setMessageFlags is not a function`.

- [ ] **Step 3: Implement**

In `api.js` after `getMailMessage`:

```js
setMessageFlags: (folder, uids, flag, value) =>
  request('PUT', '/api/Mail/Messages/Flags', { folderPath: folder, uids, flag, value }),
```

- [ ] **Step 4: Run to verify pass**

Run: `npx vitest run src/api.test.js`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/api.js src/frontend/src/api.test.js
git commit -m "api.setMessageFlags for the batch flags endpoint"
```

---

### Task 4: Frontend — pure patch functions

**Files:**
- Create: `src/frontend/src/modules/mail/list/flagPatch.ts`
- Test: `src/frontend/src/modules/mail/list/flagPatch.test.ts`

**Interfaces:**
- Consumes: `MailMessageSummary`, `MailFolderNode` from `../api/mailTypes`.
- Produces (Task 5 relies on these exact shapes):

```ts
export type MailFlagName = 'seen' | 'flagged'
export interface SummaryPatch { messages: MailMessageSummary[]; unreadDelta: number; found: number }
export function patchSummaries(messages: MailMessageSummary[], uids: number[], flag: MailFlagName, value: boolean): SummaryPatch
export function patchFolderUnread(tree: MailFolderNode[], folderPath: string, delta: number): MailFolderNode[]
```

- [ ] **Step 1: Write the failing tests**

`flagPatch.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import type { MailFolderNode, MailMessageSummary } from '../api/mailTypes'
import { patchFolderUnread, patchSummaries } from './flagPatch'

const summary = (uid: number, over: Partial<MailMessageSummary> = {}): MailMessageSummary => ({
  uid, subject: 's', fromName: 'n', fromAddress: 'a@b.c', date: '2026-07-22T10:00:00Z',
  seen: false, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '',
  ...over,
})

const node = (path: string, unread: number | null, children: MailFolderNode[] = []): MailFolderNode => ({
  path, name: path, specialUse: null, selectable: true, subscribed: true,
  total: 10, unread, uidValidity: 1, uidNext: 100, highestModSeq: null, children,
})

describe('patchSummaries', () => {
  it('rewrites only the targeted uids', () => {
    const { messages } = patchSummaries([summary(1), summary(2)], [2], 'seen', true)
    expect(messages[0].seen).toBe(false)
    expect(messages[1].seen).toBe(true)
  })

  it('counts the unread delta only for real transitions', () => {
    const input = [summary(1, { seen: true }), summary(2, { seen: false })]
    const { unreadDelta, found } = patchSummaries(input, [1, 2], 'seen', true)
    expect(unreadDelta).toBe(-1)
    expect(found).toBe(2)
  })

  it('marking unread raises the count', () => {
    const { unreadDelta } = patchSummaries([summary(1, { seen: true })], [1], 'seen', false)
    expect(unreadDelta).toBe(1)
  })

  it('flagged never moves the unread delta', () => {
    const { unreadDelta, messages } = patchSummaries([summary(1)], [1], 'flagged', true)
    expect(unreadDelta).toBe(0)
    expect(messages[0].flagged).toBe(true)
  })

  it('reports zero found when no target is present', () => {
    const { found, messages } = patchSummaries([summary(1)], [99], 'seen', true)
    expect(found).toBe(0)
    expect(messages[0].seen).toBe(false)
  })
})

describe('patchFolderUnread', () => {
  it('adjusts the one folder, deep in the tree', () => {
    const tree = [node('INBOX', 5, [node('INBOX/Sub', 3)])]
    const patched = patchFolderUnread(tree, 'INBOX/Sub', -1)
    expect(patched[0].unread).toBe(5)
    expect(patched[0].children[0].unread).toBe(2)
  })

  it('never goes below zero', () => {
    const patched = patchFolderUnread([node('INBOX', 0)], 'INBOX', -3)
    expect(patched[0].unread).toBe(0)
  })

  it('leaves a null count null', () => {
    const patched = patchFolderUnread([node('INBOX', null)], 'INBOX', -1)
    expect(patched[0].unread).toBeNull()
  })

  it('returns the tree untouched on a zero delta', () => {
    const tree = [node('INBOX', 5)]
    expect(patchFolderUnread(tree, 'INBOX', 0)).toBe(tree)
  })
})
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/modules/mail/list/flagPatch.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement**

`flagPatch.ts`:

```ts
import type { MailFolderNode, MailMessageSummary } from '../api/mailTypes'

export type MailFlagName = 'seen' | 'flagged'

export interface SummaryPatch {
  messages: MailMessageSummary[]
  /** Net unread change actually produced — re-marking a read message read counts zero. */
  unreadDelta: number
  /** How many targets were present; zero means this cache says nothing about the batch. */
  found: number
}

export function patchSummaries(
  messages: MailMessageSummary[], uids: number[], flag: MailFlagName, value: boolean,
): SummaryPatch {
  const targets = new Set(uids)
  let unreadDelta = 0
  let found = 0

  const patched = messages.map(message => {
    if (!targets.has(message.uid)) return message
    found += 1
    if (flag === 'seen') {
      if (message.seen === value) return message
      unreadDelta += value ? -1 : 1
      return { ...message, seen: value }
    }
    if (message.flagged === value) return message
    return { ...message, flagged: value }
  })

  return { messages: patched, unreadDelta, found }
}

export function patchFolderUnread(tree: MailFolderNode[], folderPath: string, delta: number): MailFolderNode[] {
  if (delta === 0) return tree

  return tree.map(node => {
    if (node.path === folderPath) {
      const unread = node.unread === null ? null : Math.max(0, node.unread + delta)
      return { ...node, unread }
    }
    return node.children.length ? { ...node, children: patchFolderUnread(node.children, folderPath, delta) } : node
  })
}
```

- [ ] **Step 4: Run to verify pass**

Run: `npx vitest run src/modules/mail/list/flagPatch.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/list/flagPatch.ts src/frontend/src/modules/mail/list/flagPatch.test.ts
git commit -m "flagPatch: pure summary and folder-unread patches with real deltas"
```

---

### Task 5: Frontend — optimistic mutation `useSetFlags`

**Files:**
- Modify: `src/frontend/src/modules/mail/queries.ts`
- Test: `src/frontend/src/modules/mail/useSetFlags.test.tsx`

**Interfaces:**
- Consumes: `api.setMessageFlags` (Task 3), `patchSummaries`/`patchFolderUnread`/`MailFlagName` (Task 4), existing `mailKeys`, `useAccountId`.
- Produces: `useSetFlags(onError?: (message: string) => void)` returning a TanStack mutation taking `SetFlagsArgs { folderPath: string; uids: number[]; flag: MailFlagName; value: boolean }`. Tasks 7, 8, 10 call `mutate(args)`.

- [ ] **Step 1: Write the failing tests**

`useSetFlags.test.tsx`. Mirror the provider wrapper and `api` mocking pattern of `src/modules/mail/list/useMessageList.test.tsx` (QueryClientProvider + whatever auth/preference mocks it uses). Core cases:

```tsx
// Seed before each case, on the keys the hook patches:
// - mailKeys.messages('primary', 'INBOX', 0, 50) -> a MailFolderPage with uid 1 unseen
// - mailKeys.messageStream('primary', 'INBOX', 100) -> { pages: [page], pageParams: [0] }
// - mailKeys.folders('primary') -> [inbox node with unread: 5]

it('patches pages, stream blocks and the folder unread count optimistically', async () => {
  // api.setMessageFlags resolves; mutate({ folderPath: 'INBOX', uids: [1], flag: 'seen', value: true })
  // then: page cache message.seen === true, stream block message.seen === true,
  // folders cache inbox.unread === 4
})

it('rolls all three caches back when the request fails', async () => {
  // api.setMessageFlags rejects; after settled: caches strictly equal the seeded values,
  // and the onError callback received 'Could not update the message'
})

it('leaves the folder count alone on a flagged mutation', async () => {
  // flag: 'flagged' -> folders cache untouched, summaries carry flagged: true
})

it('leaves the folder count alone when no cache holds the uid', async () => {
  // uids: [999] -> unread stays 5 (the poll will true it up; guessing here would drift)
})

it('never invalidates the stream key', async () => {
  // spy on queryClient.invalidateQueries; after a successful mutation it was not called
  // with any key containing 'messageStream'
})
```

Write these as real tests: seed with `queryClient.setQueryData`, read back with `queryClient.getQueryData`, use the summary/node builders from Task 4's test file (duplicate the small builders locally — test files don't import each other's helpers).

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/modules/mail/useSetFlags.test.tsx`
Expected: FAIL — `useSetFlags` is not exported.

- [ ] **Step 3: Implement**

In `queries.ts` (imports: add `InfiniteData` type from `@tanstack/react-query`, `MailFlagName`, `patchFolderUnread`, `patchSummaries` from `./list/flagPatch`, `MailMessageSummary` if needed):

```ts
export interface SetFlagsArgs {
  folderPath: string
  uids: number[]
  flag: MailFlagName
  value: boolean
}

type Snapshot = [readonly unknown[], unknown]

/**
 * Optimistic across three caches — pages, stream blocks, folder unread — with snapshot
 * rollback. Never invalidates the stream (N blocks = N IMAP connections); the 60s poll
 * and highestModSeq are the truth mechanism, so onSettled does nothing either.
 */
export function useSetFlags(onError?: (message: string) => void) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ folderPath, uids, flag, value }: SetFlagsArgs) =>
      api.setMessageFlags(folderPath, uids, flag, value),

    onMutate: async ({ folderPath, uids, flag, value }: SetFlagsArgs) => {
      const pagesKey = ['mail', accountId, 'messages', folderPath] as const
      const streamKey = ['mail', accountId, 'messageStream', folderPath] as const
      await queryClient.cancelQueries({ queryKey: pagesKey })
      await queryClient.cancelQueries({ queryKey: streamKey })

      const snapshots: Snapshot[] = []
      // The tree delta comes from the first cache that actually held a target: a cache
      // without the message knows nothing, and 0-from-absence would erase a real delta.
      let treeDelta: number | null = null

      for (const [key, page] of queryClient.getQueriesData<MailFolderPage>({ queryKey: pagesKey })) {
        if (!page) continue
        const patch = patchSummaries(page.messages, uids, flag, value)
        if (patch.found === 0) continue
        snapshots.push([key, page])
        queryClient.setQueryData(key, { ...page, messages: patch.messages })
        treeDelta ??= patch.unreadDelta
      }

      for (const [key, stream] of queryClient.getQueriesData<InfiniteData<MailFolderPage>>({ queryKey: streamKey })) {
        if (!stream) continue
        let found = 0
        let delta = 0
        const pages = stream.pages.map(page => {
          const patch = patchSummaries(page.messages, uids, flag, value)
          found += patch.found
          delta += patch.unreadDelta
          return patch.found ? { ...page, messages: patch.messages } : page
        })
        if (found === 0) continue
        snapshots.push([key, stream])
        queryClient.setQueryData(key, { ...stream, pages })
        treeDelta ??= delta
      }

      if (flag === 'seen' && treeDelta !== null && treeDelta !== 0) {
        const foldersKey = mailKeys.folders(accountId)
        const tree = queryClient.getQueryData<MailFolderNode[]>(foldersKey)
        if (tree) {
          snapshots.push([foldersKey, tree])
          queryClient.setQueryData(foldersKey, patchFolderUnread(tree, folderPath, treeDelta))
        }
      }

      return { snapshots }
    },

    onError: (_error, _args, context) => {
      for (const [key, data] of context?.snapshots ?? []) queryClient.setQueryData(key, data)
      onError?.('Could not update the message')
    },
  })
}
```

- [ ] **Step 4: Run to verify pass**

Run: `npx vitest run src/modules/mail/useSetFlags.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/queries.ts src/frontend/src/modules/mail/useSetFlags.test.tsx
git commit -m "useSetFlags: optimistic patch of pages, stream and tree, rollback on error"
```

---

### Task 6: Frontend — icons

**Files:**
- Create: `src/frontend/src/icons/StarIcon.tsx`
- Create: `src/frontend/src/icons/MailOpenIcon.tsx`
- Test: `src/frontend/src/icons/icons.test.tsx` (append)

**Interfaces:**
- Produces: `StarIcon({ size?: number; filled?: boolean })` (default 16, Feather star, `fill` follows `filled`); `MailOpenIcon({ size?: number })` (default 16, drawn in `MailIcon`'s own style — viewBox 20, stroke 1.6 — the two envelopes swap in place and a stroke-weight jump would show). Tasks 7 and 10 render them at 16.

- [ ] **Step 1: Append failing tests to `icons.test.tsx`**

Follow exactly what the file does for the other icons (render, assert svg present and size attribute). Add for `StarIcon`: renders at the given size; `filled` switches `fill` from `none` to `currentColor`. For `MailOpenIcon`: renders at the given size.

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/icons/icons.test.tsx`
Expected: FAIL — modules not found.

- [ ] **Step 3: Implement**

`StarIcon.tsx`:

```tsx
export default function StarIcon({ size = 16, filled = false }: { size?: number; filled?: boolean }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill={filled ? 'currentColor' : 'none'}
      stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" />
    </svg>
  )
}
```

`MailOpenIcon.tsx`:

```tsx
/** MailIcon's exact style (viewBox 20, stroke 1.6): the two envelopes swap in place. */
export default function MailOpenIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6">
      <path d="M2 8.5 10 3l8 5.5V16a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2Z" strokeLinecap="round" strokeLinejoin="round" />
      <path d="m2.5 9.5 7.5 5.3 7.5-5.3" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}
```

- [ ] **Step 4: Run to verify pass**

Run: `npx vitest run src/icons/icons.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/icons
git commit -m "StarIcon (filled variant) and MailOpenIcon in the house styles"
```

---

### Task 7: Frontend — list rows: star, cluster, keyboard row

**Files:**
- Modify: `src/frontend/src/modules/mail/list/MessageList.tsx`
- Modify: `src/frontend/src/modules/mail/MailLayout.tsx` (pass `onNotify={addToast}` to `MessageList`)
- Modify: `src/frontend/src/styles/mail.css`
- Test: `src/frontend/src/modules/mail/list/MessageList.test.tsx`

**Interfaces:**
- Consumes: `useSetFlags` (Task 5), `StarIcon`/`MailOpenIcon` (Task 6), existing `MailIcon`.
- Produces: `MessageList` gains prop `onNotify?: (message: string) => void`. Rows become `<div role="button" tabIndex={0}>`.

- [ ] **Step 1: Adapt/extend the tests (failing first)**

In `MessageList.test.tsx` — existing row-click tests keep passing because `getByRole('button')` matches `role="button"` divs too, but audit any `button.message-row` selector. Add (mock `useSetFlags` the way the file mocks its other query hooks, capturing `mutate`):

```tsx
it('opens a row from the keyboard', () => { /* focus row div, keyDown Enter -> onSelect called */ })

it('the star toggles flagged without opening the message', () => {
  // click button [aria-label="Star"] on an unflagged row ->
  // mutate({ folderPath, uids: [uid], flag: 'flagged', value: true }), onSelect NOT called
})

it('a flagged row offers Unstar', () => { /* aria-label is "Unstar", StarIcon filled */ })

it('the cluster toggles seen without opening the message', () => {
  // unread row: button [aria-label="Mark as read"] -> mutate({ ..., flag: 'seen', value: true })
  // read row: aria-label is "Mark as unread", value: false
})

it('mutation errors reach onNotify', () => { /* invoke the onError the hook was created with */ })
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/modules/mail/list/MessageList.test.tsx`
Expected: new tests FAIL (no star button yet).

- [ ] **Step 3: Implement the rows**

In `MessageList.tsx` — add imports (`StarIcon`, `MailOpenIcon`, `MailIcon`, `useSetFlags`, `MailMessageSummary` type), the prop, the handlers, and restructure both skins. The row `<button>` becomes a `<div>`; inner controls are real buttons that `stopPropagation`:

```tsx
interface Props {
  folderPath: string | null
  folderName?: string
  selectedUid: number | null
  onSelect: (uid: number) => void
  wide?: boolean
  onNotify?: (message: string) => void
}
```

Inside the component:

```tsx
const setFlags = useSetFlags(onNotify)

function toggle(message: MailMessageSummary, flag: 'seen' | 'flagged') {
  if (!folderPath) return
  const value = flag === 'seen' ? !message.seen : !message.flagged
  setFlags.mutate({ folderPath, uids: [message.uid], flag, value })
}

// Inner buttons handle their own keys; the row only opens when the row itself has focus.
function onRowKey(event: React.KeyboardEvent<HTMLDivElement>, uid: number) {
  if (event.target !== event.currentTarget) return
  if (event.key === 'Enter' || event.key === ' ') {
    event.preventDefault()
    onSelect(uid)
  }
}
```

Shared fragments inside the map (`message` in scope):

```tsx
const star = (
  <button
    type="button"
    className={`row-btn row-star${message.flagged ? ' is-on' : ''}`}
    aria-label={message.flagged ? 'Unstar' : 'Star'}
    onClick={event => { event.stopPropagation(); toggle(message, 'flagged') }}
  >
    <StarIcon filled={message.flagged} />
  </button>
)

const cluster = (
  <div className="message-row-cluster">
    <button
      type="button"
      className="row-btn"
      aria-label={message.seen ? 'Mark as unread' : 'Mark as read'}
      onClick={event => { event.stopPropagation(); toggle(message, 'seen') }}
    >
      {message.seen ? <MailIcon size={16} /> : <MailOpenIcon size={16} />}
    </button>
  </div>
)
```

Row wrapper (both skins):

```tsx
<div
  role="button"
  tabIndex={0}
  className={classes.join(' ')}
  onClick={() => onSelect(message.uid)}
  onKeyDown={event => onRowKey(event, message.uid)}
>
```

Narrow skin: `star` closes `.message-row-top` (after the date); `cluster` renders last, after the preview line. Wide skin: keep the existing order, then after `.message-row-date` render `cluster`, then `star`.

- [ ] **Step 4: CSS**

In `mail.css`, next to the `.message-row` block:

```css
.message-row { position: relative; }

.row-btn {
  flex: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  padding: 0;
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-muted);
  cursor: pointer;
}
.row-btn:hover { color: var(--action-primary); background: var(--surface-sunken); }

.row-star { color: var(--text-muted); }
.row-star.is-on { color: var(--badge-count-bg); }

/* Narrow skin: the cluster sits on the bottom-right corner, over the last line present. */
.message-row-cluster {
  position: absolute;
  right: 8px;
  bottom: 5px;
  display: none;
  align-items: center;
  gap: 2px;
  padding: 1px 2px;
  border-radius: var(--radius-sm);
  background: var(--surface-sunken);
}
.message-row:hover .message-row-cluster,
.message-row:focus-within .message-row-cluster { display: flex; }

/* Wide skin has no "bottom": the cluster replaces the date, the star stays at the end. */
.message-row.is-line .message-row-cluster { position: static; padding: 0; background: none; }
.message-row.is-line:hover .message-row-date,
.message-row.is-line:focus-within .message-row-date { display: none; }
.message-row.is-line .message-row-cluster { display: none; }
.message-row.is-line:hover .message-row-cluster,
.message-row.is-line:focus-within .message-row-cluster { display: flex; }
```

Verify `--radius-sm`, `--surface-sunken`, `--action-primary`, `--badge-count-bg` all exist in `src/styles/` palettes (they do — cf. the palettes spec); do not introduce any new token.

- [ ] **Step 5: Wire the toast**

In `MailLayout.tsx`, the `list` helper gains the prop:

```tsx
const list = (selected: number | null, wide: boolean) => (
  <MessageList
    folderPath={folder}
    folderName={folderName}
    selectedUid={selected}
    onSelect={selectMessage}
    wide={wide}
    onNotify={addToast}
  />
)
```

- [ ] **Step 6: Run the list suites**

Run: `npx vitest run src/modules/mail/list`
Expected: PASS — including every pre-existing MessageList test.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/modules/mail src/frontend/src/styles/mail.css
git commit -m "List rows: star and read-toggle cluster, keyboard-safe row div"
```

---

### Task 8: Frontend — mark read on open

**Files:**
- Create: `src/frontend/src/modules/mail/reader/useMarkSeenOnOpen.ts`
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx` (one hook call)
- Test: `src/frontend/src/modules/mail/reader/useMarkSeenOnOpen.test.tsx`

**Interfaces:**
- Consumes: `useSetFlags`, `useAccountId` (queries), TanStack `useQueryClient`.
- Produces:

```ts
export function findCachedSummary(queryClient: QueryClient, accountId: string, folderPath: string, uid: number): MailMessageSummary | undefined
export function useMarkSeenOnOpen(folderPath: string | null, uid: number | null, detailLoaded: boolean): void
```

Task 10 reuses `findCachedSummary` for the kebab labels.

- [ ] **Step 1: Write the failing tests**

`useMarkSeenOnOpen.test.tsx` — same wrapper/mocking approach as Task 5's test. Mock `api.setMessageFlags`; seed list caches; render the hook via a tiny host component. Cases (from the spec):

```tsx
it('fires once when the detail arrives on an unread message', ...)   // mutate called with { uids: [uid], flag: 'seen', value: true }
it('does not fire on an already-read message', ...)
it('does not fire again after Mark as unread while the uid is unchanged', ...)
  // fire once, patch cache to seen: false, re-render with detailLoaded still true -> no second call
it('fires on a deep link with no cached summary', ...)
it('re-arms when the uid changes', ...)
it('stays silent on failure', ...)  // api rejects -> no toast callback involved, nothing thrown
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/modules/mail/reader/useMarkSeenOnOpen.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement**

`useMarkSeenOnOpen.ts`:

```ts
import { useEffect, useRef } from 'react'
import { useQueryClient, type InfiniteData, type QueryClient } from '@tanstack/react-query'
import type { MailFolderPage, MailMessageSummary } from '../api/mailTypes'
import { useAccountId, useSetFlags } from '../queries'

/** The freshest cached view of one message, pages and stream blocks alike. */
export function findCachedSummary(
  queryClient: QueryClient, accountId: string, folderPath: string, uid: number,
): MailMessageSummary | undefined {
  for (const [, page] of queryClient.getQueriesData<MailFolderPage>(
    { queryKey: ['mail', accountId, 'messages', folderPath] })) {
    const hit = page?.messages.find(m => m.uid === uid)
    if (hit) return hit
  }
  for (const [, stream] of queryClient.getQueriesData<InfiniteData<MailFolderPage>>(
    { queryKey: ['mail', accountId, 'messageStream', folderPath] })) {
    for (const page of stream?.pages ?? []) {
      const hit = page.messages.find(m => m.uid === uid)
      if (hit) return hit
    }
  }
  return undefined
}

/**
 * Marks a message read once per opening — armed on uid change, fired when the detail
 * arrives. Failure is silent by design: the next poll corrects it.
 */
export function useMarkSeenOnOpen(folderPath: string | null, uid: number | null, detailLoaded: boolean) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()
  const { mutate } = useSetFlags()
  const firedFor = useRef<string | null>(null)

  useEffect(() => {
    if (!folderPath || uid === null || !detailLoaded) return
    const opening = `${folderPath} ${uid}`
    if (firedFor.current === opening) return
    firedFor.current = opening

    // No cached summary (deep link) fires too: an idempotent STORE \Seen costs nothing.
    const summary = findCachedSummary(queryClient, accountId, folderPath, uid)
    if (summary?.seen) return

    mutate({ folderPath, uids: [uid], flag: 'seen', value: true })
  }, [folderPath, uid, detailLoaded, accountId, queryClient, mutate])
}
```

In `MessageReader.tsx`, with the other hooks at the top (before any early return):

```tsx
useMarkSeenOnOpen(folderPath, uid, Boolean(data))
```

- [ ] **Step 4: Run to verify pass**

Run: `npx vitest run src/modules/mail/reader`
Expected: PASS, existing reader tests included.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/reader
git commit -m "Mark a message read once per opening, silently on failure"
```

---

### Task 9: Frontend — generic DropdownMenu

**Files:**
- Create: `src/frontend/src/components/DropdownMenu.tsx`
- Modify: `src/frontend/src/styles/index.css` or the stylesheet holding `.avatar-menu` (same file, new block)
- Test: `src/frontend/src/components/DropdownMenu.test.tsx`

**Interfaces:**
- Produces:

```tsx
export interface MenuItem { label: string; icon?: ReactNode; onSelect: () => void }
interface Props { ariaLabel: string; trigger: ReactNode; items: MenuItem[]; className?: string }
export default function DropdownMenu(props: Props)
```

Task 10 renders it with the kebab as `trigger`.

- [ ] **Step 1: Write the failing tests**

```tsx
it('opens on click and lists the items', ...)          // click [aria-label] -> role="menu" with both labels
it('closes on outside mousedown', ...)                 // fireEvent.mouseDown(document.body)
it('closes on Escape', ...)
it('an item click closes and fires its action', ...)   // onSelect called once, menu gone
it('reflects state through aria-expanded', ...)
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/components/DropdownMenu.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement**

```tsx
import { type ReactNode, useEffect, useRef, useState } from 'react'

export interface MenuItem {
  label: string
  icon?: ReactNode
  onSelect: () => void
}

interface Props {
  ariaLabel: string
  trigger: ReactNode
  items: MenuItem[]
  className?: string
}

/** Click-toggled dropdown on the AvatarMenu pattern: outside mousedown and Escape close it. */
export default function DropdownMenu({ ariaLabel, trigger, items, className }: Props) {
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    function onMouseDown(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false)
    }
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', onMouseDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onMouseDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  return (
    <div className="dropdown-root" ref={rootRef}>
      <button type="button" className={className} aria-label={ariaLabel} aria-expanded={open}
        onClick={() => setOpen(o => !o)}>
        {trigger}
      </button>
      {open && (
        <div className="dropdown-menu" role="menu">
          {items.map(item => (
            <button key={item.label} type="button" role="menuitem" className="dropdown-item"
              onClick={() => { setOpen(false); item.onSelect() }}>
              {item.icon}
              {item.label}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
```

CSS (same stylesheet as `.avatar-menu`, matching its surface/shadow tokens):

```css
.dropdown-root { position: relative; }

.dropdown-menu {
  position: absolute;
  top: calc(100% + 4px);
  right: 0;
  z-index: 20;
  min-width: 180px;
  padding: 4px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  box-shadow: 0 4px 16px rgb(0 0 0 / 0.18);
}

.dropdown-item {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 7px 10px;
  border: none;
  border-radius: var(--radius-sm);
  background: none;
  color: var(--text);
  font: inherit;
  text-align: left;
  cursor: pointer;
}
.dropdown-item:hover { background: var(--surface-sunken); }
```

If `.avatar-menu` already carries a shadow token or literal, mirror it exactly rather than inventing a second shadow.

- [ ] **Step 4: Run to verify pass**

Run: `npx vitest run src/components/DropdownMenu.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/components/DropdownMenu.tsx src/frontend/src/components/DropdownMenu.test.tsx src/frontend/src/styles
git commit -m "Generic DropdownMenu on the AvatarMenu pattern"
```

---

### Task 10: Frontend — the kebab comes alive

**Files:**
- Modify: `src/frontend/src/modules/mail/reader/ReaderActions.tsx`
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx`
- Modify: `src/frontend/src/modules/mail/MailLayout.tsx` (pass `onNotify={addToast}` to the three `MessageReader` instances)
- Test: `src/frontend/src/modules/mail/reader/ReaderActions.test.tsx`, `src/frontend/src/modules/mail/reader/MessageReader.test.tsx`

**Interfaces:**
- Consumes: `DropdownMenu` (Task 9), `useSetFlags` (Task 5), `findCachedSummary` (Task 8), `StarIcon`/`MailOpenIcon` (Task 6), existing `MailIcon`, `KebabIcon`.
- Produces: `ReaderActions` props extended with `seen: boolean; flagged: boolean; onToggleSeen: () => void; onToggleFlagged: () => void`. `MessageReader` gains prop `onNotify?: (message: string) => void`.

- [ ] **Step 1: Adapt/extend the tests (failing first)**

`ReaderActions.test.tsx` — keep every existing case (colour toggle, rule, kebab presence via `aria-label="Message actions"`). Add:

```tsx
it('the kebab opens a menu with the two flag entries', ...)
  // seen: true, flagged: false -> items "Mark as unread" and "Star"
it('labels follow the state', ...)          // seen: false, flagged: true -> "Mark as read", "Unstar"
it('entries fire their callbacks and close', ...)
```

The old "kebab without handler doesn't throw" test becomes the open test — the kebab now has a handler by design.

`MessageReader.test.tsx` — add: opening the kebab shows `Mark as unread` (cache summary seen) and clicking it calls the mutation with `{ uids: [uid], flag: 'seen', value: false }`. Mock `useSetFlags` as in Task 7.

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/modules/mail/reader`
Expected: new cases FAIL.

- [ ] **Step 3: Implement**

`ReaderActions.tsx` — extended props and the kebab wrapped:

```tsx
import DropdownMenu from '../../../components/DropdownMenu'
import MailIcon from '../../../icons/MailIcon'
import MailOpenIcon from '../../../icons/MailOpenIcon'
import StarIcon from '../../../icons/StarIcon'

interface Props {
  showColourToggle: boolean
  originalColours: boolean
  onToggleColours: () => void
  seen: boolean
  flagged: boolean
  onToggleSeen: () => void
  onToggleFlagged: () => void
}
```

The kebab button is replaced by (colour toggle and rule unchanged):

```tsx
<DropdownMenu
  ariaLabel="Message actions"
  className="action-btn"
  trigger={<KebabIcon />}
  items={[
    {
      label: seen ? 'Mark as unread' : 'Mark as read',
      icon: seen ? <MailIcon size={16} /> : <MailOpenIcon size={16} />,
      onSelect: onToggleSeen,
    },
    {
      label: flagged ? 'Unstar' : 'Star',
      icon: <StarIcon filled={flagged} />,
      onSelect: onToggleFlagged,
    },
  ]}
/>
```

`MessageReader.tsx` — prop `onNotify?: (message: string) => void`; near the other hooks:

```tsx
const accountId = useAccountId()
const queryClient = useQueryClient()
const setFlags = useSetFlags(onNotify)
```

Before the return (after the early returns — `folderPath`/`uid` are non-null there):

```tsx
// Labels read the list cache at render time; the menu opens through a state change, so
// they are fresh at every opening. No summary (deep link): read and unstarred, since the
// opening itself just marked it read.
const summary = findCachedSummary(queryClient, accountId, folderPath!, uid!)
const seen = summary?.seen ?? true
const flagged = summary?.flagged ?? false
```

And the render:

```tsx
<ReaderActions
  showColourToggle={isDark && !!data.htmlBody}
  originalColours={originalColours}
  onToggleColours={() => setOriginalColours(v => !v)}
  seen={seen}
  flagged={flagged}
  onToggleSeen={() => setFlags.mutate({ folderPath: folderPath!, uids: [uid!], flag: 'seen', value: !seen })}
  onToggleFlagged={() => setFlags.mutate({ folderPath: folderPath!, uids: [uid!], flag: 'flagged', value: !flagged })}
/>
```

`MailLayout.tsx` — each of the three `<MessageReader …/>` gains `onNotify={addToast}`.

- [ ] **Step 4: Run to verify pass**

Run: `npx vitest run src/modules/mail`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail src/frontend/src/components
git commit -m "Kebab menu: mark read/unread and star from the reader"
```

---

### Task 11: Full verification

**Files:** none new.

- [ ] **Step 1: Backend suite**

Run (in `src/snoopy.microservice`): `dotnet test`
Expected: PASS, zero failures.

- [ ] **Step 2: Frontend suite, typecheck, lint, build**

Run (in `src/frontend`): `npx vitest run && npm run build && npx eslint src`
Expected: all green. Fix anything that surfaces; re-run until clean.

- [ ] **Step 3: Commit leftovers**

If verification produced fixes (or a regenerated `ApiDocumentation.xml` was missed):

```bash
git add -A
git commit -m "2b1 verification fixes"
```

---

## Self-review notes

- Spec §3 (endpoint, validation, ReadWrite, silent, no uidValidity guard) → Tasks 1–2.
- Spec §4 (api.js, flagPatch, useSetFlags, useMarkSeenOnOpen) → Tasks 3–5, 8.
- Spec §5 (row structure, star, cluster, both skins, DropdownMenu, kebab entries, icons) → Tasks 6–7, 9–10.
- Spec §6 tests → per task; the ImapSessionTests divergence is documented under Global Constraints.
- Type names cross-checked: `MailFlagName`, `SetFlagsArgs`, `SummaryPatch`, `findCachedSummary`, `MenuItem` are each defined once and consumed by name in later tasks.
