# Multi-select messages (2b3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add checkbox multi-selection to the message list with a persistent bulk-action toolbar, plus a new "empty folder" action (purge trash/junk, move-all elsewhere) surfaced in a kebab and a pinned trash/junk banner.

**Architecture:** The 2b2 data layer already batches (`useSetFlags`/`useMoveMessages`/`useDeleteMessages` take `uids: number[]`), so the bulk actions are pure wiring over a new UI selection layer. The only new backend capability is emptying a whole folder, unbounded by the 200-UID cap. Selection state is a pure `useSelection` hook; the toolbar and banner are dumb components driven by props; `MessageList` wires them together.

**Tech Stack:** React 18 + TypeScript, TanStack Query v5, Vitest + jsdom + @testing-library/react (frontend); ASP.NET Core .NET 10 + MailKit, xUnit + Moq (backend).

Spec: `docs/superpowers/specs/2026-07-23-webmail-multiselect-2b3-design.md`.

## Global Constraints

- **Tokens only, never a colour literal**; row/toolbar geometry **measured in a real browser** (jsdom does no layout), never assumed.
- **Never `invalidateQueries` on the `messageStream` key** (N blocks = N IMAP connections). Target caches are **dropped** (`removeQueries`/`dropFolderCaches`), never invalidated.
- **`settle()`** (from `src/test-utils.ts`) before **every** silence assertion — TanStack v5 notifies observers on a macrotask.
- Every write mutation carries the shared key **`mailKeys.writes(accountId)`** so `useListRefresh` stands down — including `useEmptyFolder`.
- Bulk-action UID batches are capped at **200** (backend refuses `<1 or >200`); "empty folder" **bypasses** this — it operates server-side on `1:*`.
- Backend: `dotnet test` (never `--no-build`) when a test file is added; `Assert.IsType<BadRequestObjectResult>` for 400s via `BadRequest(body)`; folder paths in the request **body**, never a route segment; file-scoped namespaces, primary constructors, records for DTOs, `sealed`, `Async` suffix, `var` for obvious types.
- Validation order for the empty endpoint mirrors Move: source blank → (if target present) target==source → credentials → repository.
- UI strings in **English**; commit messages concise (2 lines body max), and **never start or end a commit message with `@`** (use `git commit -F -` with a heredoc in Bash).

---

## File Structure

**Backend (`src/snoopy.microservice/`)**
- `Models/Mail/MessageRequests.cs` — add `EmptyFolderRequest` record/class.
- `Services/IImapSession.cs` + `Services/ImapSession.cs` — add `EmptyAsync(folderPath, targetPath?, ct)`.
- `Repositories/IMailMessageRepository.cs` + `Repositories/MailMessageRepository.cs` — add `EmptyAsync(...)` delegating to the session.
- `Controllers/MailController.cs` — add `POST api/Mail/Folders/Empty` → `EmptyFolder`.
- Tests: `snoopy.microservice.Tests/Controllers/MailControllerTests.cs` (+ `Services/ImapSessionTests.cs` if a client mock exists).

**Frontend (`src/frontend/src/`)**
- `api.js` — add `emptyFolder(folder, targetFolder)`.
- `modules/mail/queries.ts` — add `useEmptyFolder(onError?)` + optimistic cache; test `modules/mail/useEmptyFolder.test.tsx`.
- `modules/mail/list/useSelection.ts` — pure selection hook; test `useSelection.test.ts`.
- `modules/mail/list/SelectionToolbar.tsx` — the toolbar band; test `SelectionToolbar.test.tsx`.
- `modules/mail/list/EmptyFolderBanner.tsx` — the pinned trash/junk banner; test `EmptyFolderBanner.test.tsx`.
- `modules/mail/list/MessageList.tsx` — wire checkboxes, toolbar, banner, bulk actions, empty folder; tests in `MessageList.test.tsx`.
- CSS travels with each component (checkbox column, toolbar, banner) in the existing mail stylesheet.

---

## Task 1: Backend — empty-folder endpoint (purge / move-all)

**Files:**
- Modify: `src/snoopy.microservice/Models/Mail/MessageRequests.cs`
- Modify: `src/snoopy.microservice/Services/IImapSession.cs`, `src/snoopy.microservice/Services/ImapSession.cs`
- Modify: `src/snoopy.microservice/Repositories/IMailMessageRepository.cs`, `src/snoopy.microservice/Repositories/MailMessageRepository.cs`
- Modify: `src/snoopy.microservice/Controllers/MailController.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs`

**Interfaces:**
- Produces (frontend Task 2 consumes the HTTP contract): `POST /api/Mail/Folders/Empty`, body `{ folderPath: string, targetFolderPath: string | null }`, 204 on success, 400 (source blank / target==source / target not selectable), 401, 502.
- Produces: `IImapSession.EmptyAsync(string folderPath, string? targetPath, CancellationToken)` → `Task<Result>`; `IMailMessageRepository.EmptyAsync(User, string password, string folderPath, string? targetPath, CancellationToken)` → `Task<Result>`.

- [ ] **Step 1: Add the request DTO**

In `Models/Mail/MessageRequests.cs`, append:

```csharp
/// <summary>
/// Empties an entire folder. Unbounded by the 200-UID cap — it operates on 1:* server-side.
/// A null/blank <see cref="TargetFolderPath"/> means purge (permanent expunge of everything);
/// a target means move every message there (used to move a normal folder's contents to trash).
/// </summary>
public sealed class EmptyFolderRequest
{
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>Null or blank = purge. Set = move all messages into this folder.</summary>
    public string? TargetFolderPath { get; set; }
}
```

- [ ] **Step 2: Add `EmptyAsync` to the session interface**

In `Services/IImapSession.cs`, after `DeleteAsync` (near line 80):

```csharp
    /// <summary>
    /// Empties a whole folder. A null/blank <paramref name="targetPath"/> purges it
    /// (mark 1:* \Deleted + EXPUNGE, no UIDPLUS needed — the whole folder is expunged, not a
    /// subset). A target moves every message there, failing with
    /// ImapSession.TargetNotSelectable when the target cannot hold messages.
    /// </summary>
    Task<Result> EmptyAsync(string folderPath, string? targetPath, CancellationToken cancellationToken);
```

- [ ] **Step 3: Implement `EmptyAsync` on the session**

In `Services/ImapSession.cs`, after `DeleteAsync` (near line 337). Note MailKit: `SearchAsync(SearchQuery.All)` returns the folder's UIDs; a bare `ExpungeAsync()` purges every `\Deleted` message (safe here because we just marked them all, and it needs no UIDPLUS).

```csharp
    public async Task<Result> EmptyAsync(string folderPath, string? targetPath, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var move = !string.IsNullOrWhiteSpace(targetPath);

        try
        {
            IMailFolder? target = null;
            if (move)
            {
                try { target = await _client.GetFolderAsync(targetPath!, cancellationToken); }
                catch (FolderNotFoundException) { return Result.Failure(TargetNotSelectable); }

                if ((target.Attributes & (FolderAttributes.NoSelect | FolderAttributes.NonExistent)) != 0)
                    return Result.Failure(TargetNotSelectable);
            }

            var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var uids = await folder.SearchAsync(SearchQuery.All, cancellationToken);
            if (uids.Count == 0) return Result.Success();

            if (move)
            {
                await folder.MoveToAsync(uids, target!, cancellationToken);
            }
            else
            {
                // Bare EXPUNGE purges every \Deleted message; emptying purges the whole folder,
                // so no UID EXPUNGE (UIDPLUS) is needed — unlike DeleteAsync which targets a subset.
                await folder.AddFlagsAsync(uids, MessageFlags.Deleted, silent: true, cancellationToken);
                await folder.ExpungeAsync(cancellationToken);
            }

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
            _logger.LogError(ex, "Failed to empty {Folder} (move: {Move})", folderPath, move);
            return Result.Failure("Unable to empty the folder");
        }
    }
```

- [ ] **Step 4: Add `EmptyAsync` to the repository interface + impl**

In `Repositories/IMailMessageRepository.cs`, after `DeleteAsync`:

```csharp
    /// <summary>Empties a whole folder: purge (no target) or move every message to a target.</summary>
    Task<Result> EmptyAsync(User user, string password, string folderPath, string? targetPath, CancellationToken cancellationToken);
```

In `Repositories/MailMessageRepository.cs`, after `DeleteAsync` (mirror its shape — open the session, delegate):

```csharp
    public async Task<Result> EmptyAsync(User user, string password, string folderPath, string? targetPath, CancellationToken cancellationToken)
    {
        var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
        if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
        await using var session = sessionResult.Value;
        return await session.EmptyAsync(folderPath, targetPath, cancellationToken);
    }
```

(Match the exact `_factory.OpenAsync` / `IsFailure` / `await using` shape already used by `DeleteAsync` in that file.)

- [ ] **Step 5: Write the failing controller tests**

In `MailControllerTests.cs`, next to the `Move_*` tests, add:

```csharp
    [Fact]
    public async Task EmptyFolder_Returns204AndDelegatesPurgeWhenNoTarget()
    {
        _messages.Setup(m => m.EmptyAsync(It.IsAny<User>(), "hunter2", "Trash", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().EmptyFolder(
            new EmptyFolderRequest { FolderPath = "Trash", TargetFolderPath = null }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsType<StatusCodeResult>(result).StatusCode);
        _messages.Verify(m => m.EmptyAsync(It.IsAny<User>(), "hunter2", "Trash", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmptyFolder_DelegatesMoveWhenTargetGiven()
    {
        _messages.Setup(m => m.EmptyAsync(It.IsAny<User>(), "hunter2", "Projects", "Trash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().EmptyFolder(
            new EmptyFolderRequest { FolderPath = "Projects", TargetFolderPath = "Trash" }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsType<StatusCodeResult>(result).StatusCode);
        _messages.Verify(m => m.EmptyAsync(It.IsAny<User>(), "hunter2", "Projects", "Trash", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmptyFolder_Returns400ForABlankSourceWithoutReachingTheRepository()
    {
        var result = await CreateController().EmptyFolder(
            new EmptyFolderRequest { FolderPath = " ", TargetFolderPath = null }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _messages.Verify(m => m.EmptyAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmptyFolder_Returns400WhenTargetEqualsSource()
    {
        var result = await CreateController().EmptyFolder(
            new EmptyFolderRequest { FolderPath = "Projects", TargetFolderPath = "Projects" }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("The target folder must differ from the source folder", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task EmptyFolder_Returns401WhenCredentialsAreUnavailable()
    {
        var controller = CreateController();
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
            .Returns(Result.Failure<string>("credentials_unavailable"));

        var result = await controller.EmptyFolder(
            new EmptyFolderRequest { FolderPath = "Trash", TargetFolderPath = null }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task EmptyFolder_Returns400WhenTargetIsNotSelectable()
    {
        _messages.Setup(m => m.EmptyAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ImapSession.TargetNotSelectable));

        var result = await CreateController().EmptyFolder(
            new EmptyFolderRequest { FolderPath = "Projects", TargetFolderPath = "NoSelect" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task EmptyFolder_Returns502WhenTheServerRefuses()
    {
        _messages.Setup(m => m.EmptyAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Unable to empty the folder"));

        var result = await CreateController().EmptyFolder(
            new EmptyFolderRequest { FolderPath = "Trash", TargetFolderPath = null }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status502BadGateway, Assert.IsType<ObjectResult>(result).StatusCode);
    }
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MailControllerTests.EmptyFolder"`
Expected: FAIL — `MailController` has no `EmptyFolder` method (compile error / missing member).

- [ ] **Step 7: Add the controller action**

In `Controllers/MailController.cs`, after `DeleteMessages` (near line 583). Validation order mirrors `MoveOrCopy`: source blank → target==source (only when a target is given) → credentials → repository; `TargetNotSelectable` → 400.

```csharp
    /// <summary>Empties a whole folder: purge (no target) or move every message to a target.</summary>
    /// <param name="request">source folder and optional target (blank = purge)</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">The folder was emptied</response>
    /// <response code="400">The source is missing, the target equals the source, or the target cannot hold messages</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="502">The mail server could not be reached</response>
    [HttpPost("Folders/Empty")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> EmptyFolder(EmptyFolderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FolderPath))
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));
        if (!string.IsNullOrWhiteSpace(request.TargetFolderPath)
            && string.Equals(request.FolderPath, request.TargetFolderPath, StringComparison.Ordinal))
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("The target folder must differ from the source folder"));

        var password = _credentials.Retrieve(Request);
        if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

        var result = await _messages.EmptyAsync(
            AuthenticatedUser, password.Value, request.FolderPath, request.TargetFolderPath, cancellationToken);

        if (result.IsFailure && result.Error == ImapSession.TargetNotSelectable)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("The target folder cannot hold messages"));

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway, successStatusCode: StatusCodes.Status204NoContent);
    }
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test` (full suite — a new test file/members were added, so no `--no-build`)
Expected: PASS, including all seven `EmptyFolder_*` tests; no regression.

- [ ] **Step 9: Commit**

```bash
git add src/snoopy.microservice/Models/Mail/MessageRequests.cs src/snoopy.microservice/Services/IImapSession.cs src/snoopy.microservice/Services/ImapSession.cs src/snoopy.microservice/Repositories/IMailMessageRepository.cs src/snoopy.microservice/Repositories/MailMessageRepository.cs src/snoopy.microservice/Controllers/MailController.cs src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs
git commit -F - <<'EOF'
Backend 2b3: empty-folder endpoint (purge / move-all)

POST /api/Mail/Folders/Empty; purge marks 1:* \Deleted + EXPUNGE (no
UIDPLUS), a target moves all messages there. Unbounded by the 200-UID cap.
EOF
```

---

## Task 2: Frontend data layer — `useEmptyFolder` + api

**Files:**
- Modify: `src/frontend/src/api.js` (near the other mail methods, ~line 239)
- Modify: `src/frontend/src/modules/mail/queries.ts`
- Test: `src/frontend/src/modules/mail/useEmptyFolder.test.tsx` (new; mirror `useMoveMessages.test.tsx`)

**Interfaces:**
- Consumes: `POST /api/Mail/Folders/Empty` (Task 1); `dropFolderCaches`, `cancelListQueries`, `patchTreeCounts`, `mailKeys.writes`, `type Snapshot`, `flatten` (from `./folders/folderNodes`).
- Produces: `useEmptyFolder(onError?)` → mutation whose `mutate` takes `EmptyFolderArgs { folderPath: string; targetFolderPath?: string | null }`.

- [ ] **Step 1: Add the api method**

In `src/frontend/src/api.js`, after `deleteMessages` (line ~239):

```javascript
  emptyFolder: (folder, targetFolder) =>
    request('POST', '/api/Mail/Folders/Empty', { folderPath: folder, targetFolderPath: targetFolder ?? null }),
```

- [ ] **Step 2: Write the failing hook test**

Create `src/frontend/src/modules/mail/useEmptyFolder.test.tsx` (model the harness on `useMoveMessages.test.tsx`: a `QueryClient`, `renderHook`, `api` mocked). Seed the source folder's list caches and the tree, empty it, and assert the source caches are dropped and the tree zeroed; in move mode assert the trash gains the source's counts.

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useEmptyFolder, mailKeys } from './queries'
import { settle } from '../../test-utils'

const mocks = vi.hoisted(() => ({ emptyFolder: vi.fn() }))
vi.mock('../../api.js', () => ({ api: mocks }))
vi.mock('../../contexts/AuthContext', () => ({ useAuth: () => ({ activeAccount: { id: 'primary' } }) }))
vi.mock('../../hooks/usePreferences', () => ({
  usePreferences: () => ({ data: {} }), notifiesOf: () => false,
}))

const ACC = 'primary'
function seededClient() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  client.setQueryData(mailKeys.messages(ACC, 'Trash', 0, 50), {
    messages: [{ uid: 5, seen: false }, { uid: 6, seen: true }], total: 2, page: 0, pageSize: 50,
  })
  client.setQueryData(mailKeys.folders(ACC), [
    { path: 'Trash', name: 'Trash', specialUse: 'trash', total: 2, unread: 1, children: [] },
    { path: 'Projects', name: 'Projects', specialUse: null, total: 3, unread: 2, children: [] },
  ])
  return client
}
function wrapperFor(client: QueryClient) {
  return ({ children }: { children: ReactNode }) =>
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

beforeEach(() => { vi.clearAllMocks(); mocks.emptyFolder.mockResolvedValue(undefined) })

describe('useEmptyFolder', () => {
  it('purges: drops the source caches and zeroes its counts', async () => {
    const client = seededClient()
    const { result } = renderHook(() => useEmptyFolder(), { wrapper: wrapperFor(client) })

    await act(async () => { result.current.mutate({ folderPath: 'Trash' }); await settle() })

    expect(mocks.emptyFolder).toHaveBeenCalledWith('Trash', null)
    expect(client.getQueryData(mailKeys.messages(ACC, 'Trash', 0, 50))).toBeUndefined()
    const tree = client.getQueryData(mailKeys.folders(ACC)) as { path: string; total: number; unread: number }[]
    const trash = tree.find(n => n.path === 'Trash')!
    expect(trash.total).toBe(0)
    expect(trash.unread).toBe(0)
  })

  it('moves: zeroes the source and adds its counts to the target', async () => {
    const client = seededClient()
    client.setQueryData(mailKeys.messages(ACC, 'Projects', 0, 50), {
      messages: [{ uid: 1, seen: false }], total: 3, page: 0, pageSize: 50,
    })
    const { result } = renderHook(() => useEmptyFolder(), { wrapper: wrapperFor(client) })

    await act(async () => { result.current.mutate({ folderPath: 'Projects', targetFolderPath: 'Trash' }); await settle() })

    expect(mocks.emptyFolder).toHaveBeenCalledWith('Projects', 'Trash')
    const tree = client.getQueryData(mailKeys.folders(ACC)) as { path: string; total: number; unread: number }[]
    expect(tree.find(n => n.path === 'Projects')!.total).toBe(0)
    expect(tree.find(n => n.path === 'Trash')!.total).toBe(5) // 2 + 3
    expect(tree.find(n => n.path === 'Trash')!.unread).toBe(3) // 1 + 2
  })

  it('rolls the caches back when the request fails', async () => {
    const client = seededClient()
    mocks.emptyFolder.mockRejectedValue(new Error('nope'))
    const { result } = renderHook(() => useEmptyFolder(), { wrapper: wrapperFor(client) })

    await act(async () => { result.current.mutate({ folderPath: 'Trash' }); await settle() })

    expect(client.getQueryData(mailKeys.messages(ACC, 'Trash', 0, 50))).toBeDefined()
    const tree = client.getQueryData(mailKeys.folders(ACC)) as { path: string; total: number }[]
    expect(tree.find(n => n.path === 'Trash')!.total).toBe(2)
  })
})
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd src/frontend && npx vitest run src/modules/mail/useEmptyFolder.test.tsx`
Expected: FAIL — `useEmptyFolder` is not exported from `queries.ts`.

- [ ] **Step 4: Implement `useEmptyFolder`**

In `src/frontend/src/modules/mail/queries.ts`: add `import { flatten } from './folders/folderNodes'` to the imports, and append the hook after `useDeleteMessages`:

```typescript
export interface EmptyFolderArgs {
  folderPath: string
  /** Blank/absent = purge; set = move every message into this folder. */
  targetFolderPath?: string | null
}

/**
 * Empties a whole folder. The source's caches are dropped and its counts zeroed; a move also
 * adds the source's own total/unread (read from the tree node) to the target. Optimistic with
 * snapshot rollback, never an invalidate — the 60s poll reconciles.
 */
export function useEmptyFolder(onError?: (message: string) => void) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: ({ folderPath, targetFolderPath }: EmptyFolderArgs) =>
      api.emptyFolder(folderPath, targetFolderPath ?? null),

    onMutate: async ({ folderPath, targetFolderPath }: EmptyFolderArgs) => {
      await cancelListQueries(queryClient, accountId, folderPath)

      const snapshots: Snapshot[] = dropFolderCaches(queryClient, accountId, folderPath)

      // The source folder's own counts drive both the zeroing and, on a move, the target's gain.
      const tree = queryClient.getQueryData<MailFolderNode[]>(mailKeys.folders(accountId))
      const node = tree ? flatten(tree).find(entry => entry.node.path === folderPath)?.node : undefined
      const source = { total: node?.total ?? 0, unread: node?.unread ?? 0 }

      const patches: [string, FolderCountDeltas][] = [
        [folderPath, { total: -source.total, unread: -source.unread }],
      ]

      const move = !!targetFolderPath
      if (move) {
        await cancelListQueries(queryClient, accountId, targetFolderPath!)
        snapshots.push(...dropFolderCaches(queryClient, accountId, targetFolderPath!))
        patches.push([targetFolderPath!, { total: source.total, unread: source.unread }])
      }

      snapshots.push(...patchTreeCounts(queryClient, accountId, patches))
      return { snapshots }
    },

    onError: (_error, _args, context) => {
      for (const [key, data] of context?.snapshots ?? []) queryClient.setQueryData(key, data)
      onError?.('Could not empty the folder')
    },
  })
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd src/frontend && npx vitest run src/modules/mail/useEmptyFolder.test.tsx`
Expected: PASS (all three cases).

- [ ] **Step 6: Break-verify the move-target patch**

Temporarily change `patches.push([targetFolderPath!, { total: source.total, unread: source.unread }])` to `{ total: 0, unread: 0 }`; rerun — the "moves" test must fail on the trash total (`expected 2 to be 5`). Restore.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/api.js src/frontend/src/modules/mail/queries.ts src/frontend/src/modules/mail/useEmptyFolder.test.tsx
git commit -F - <<'EOF'
Frontend 2b3: useEmptyFolder hook + api

Optimistic empty: drop source caches, zero its counts; a move adds the
source's total/unread (from the tree node) to the target. Carries mailKeys.writes.
EOF
```

---

## Task 3: `useSelection` — the pure selection hook

**Files:**
- Create: `src/frontend/src/modules/mail/list/useSelection.ts`
- Test: `src/frontend/src/modules/mail/list/useSelection.test.ts`

**Interfaces:**
- Produces: `useSelection(resetKey: string)` → `{ selected: Set<number>; has(uid: number): boolean; toggle(uid: number, index: number): void; toggleRange(loadedUids: number[], index: number): void; selectAll(loadedUids: number[]): void; clear(): void }`. The hook clears when `resetKey` changes. It never stores `loadedUids`, so consumers derive the effective selection as `selected ∩ loadedUids` — a departed row simply stops counting.

- [ ] **Step 1: Write the failing test**

Create `src/frontend/src/modules/mail/list/useSelection.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { useSelection } from './useSelection'

const LOADED = [10, 20, 30, 40, 50]

describe('useSelection', () => {
  it('toggles a uid on and off', () => {
    const { result } = renderHook(() => useSelection('INBOX::0'))
    act(() => result.current.toggle(20, 1))
    expect(result.current.has(20)).toBe(true)
    act(() => result.current.toggle(20, 1))
    expect(result.current.has(20)).toBe(false)
  })

  it('selects the inclusive range from the last toggled anchor', () => {
    const { result } = renderHook(() => useSelection('INBOX::0'))
    act(() => result.current.toggle(20, 1))          // anchor at index 1
    act(() => result.current.toggleRange(LOADED, 3)) // 1..3 → 20,30,40
    expect([...result.current.selected].sort((a, b) => a - b)).toEqual([20, 30, 40])
  })

  it('ranges upward too (anchor after the target)', () => {
    const { result } = renderHook(() => useSelection('INBOX::0'))
    act(() => result.current.toggle(40, 3))
    act(() => result.current.toggleRange(LOADED, 1)) // 3..1 → 20,30,40
    expect([...result.current.selected].sort((a, b) => a - b)).toEqual([20, 30, 40])
  })

  it('selectAll takes every loaded uid; clear empties it', () => {
    const { result } = renderHook(() => useSelection('INBOX::0'))
    act(() => result.current.selectAll(LOADED))
    expect(result.current.selected.size).toBe(5)
    act(() => result.current.clear())
    expect(result.current.selected.size).toBe(0)
  })

  it('clears when the resetKey changes (folder or page)', () => {
    let key = 'INBOX::0'
    const { result, rerender } = renderHook(() => useSelection(key))
    act(() => result.current.selectAll(LOADED))
    expect(result.current.selected.size).toBe(5)
    key = 'INBOX::1'
    rerender()
    expect(result.current.selected.size).toBe(0)
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/frontend && npx vitest run src/modules/mail/list/useSelection.test.ts`
Expected: FAIL — `useSelection` does not exist.

- [ ] **Step 3: Implement the hook**

Create `src/frontend/src/modules/mail/list/useSelection.ts`:

```ts
import { useEffect, useRef, useState } from 'react'

/**
 * Checkbox selection over the loaded rows. `resetKey` (folder + page) clears it; the hook never
 * stores the row list, so the caller intersects `selected` with what is on screen — a departed
 * row stops counting on its own. `toggleRange` selects the inclusive slice from the last-toggled
 * anchor to `index`, over the `loadedUids` order the caller passes in.
 */
export function useSelection(resetKey: string) {
  const [selected, setSelected] = useState<Set<number>>(() => new Set())
  const anchor = useRef<number | null>(null)

  useEffect(() => {
    setSelected(new Set())
    anchor.current = null
  }, [resetKey])

  return {
    selected,
    has: (uid: number) => selected.has(uid),
    toggle(uid: number, index: number) {
      setSelected(prev => {
        const next = new Set(prev)
        if (next.has(uid)) next.delete(uid); else next.add(uid)
        return next
      })
      anchor.current = index
    },
    toggleRange(loadedUids: number[], index: number) {
      const from = anchor.current ?? index
      const [lo, hi] = from <= index ? [from, index] : [index, from]
      setSelected(prev => new Set([...prev, ...loadedUids.slice(lo, hi + 1)]))
      anchor.current = index
    },
    selectAll(loadedUids: number[]) {
      setSelected(new Set(loadedUids))
      anchor.current = null
    },
    clear() {
      setSelected(new Set())
      anchor.current = null
    },
  }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/frontend && npx vitest run src/modules/mail/list/useSelection.test.ts`
Expected: PASS (all five cases).

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/list/useSelection.ts src/frontend/src/modules/mail/list/useSelection.test.ts
git commit -F - <<'EOF'
Frontend 2b3: useSelection hook

Set-based checkbox selection with shift-range anchor; clears on resetKey
(folder + page). Caller intersects with loaded rows so departed rows drop out.
EOF
```

---

## Task 4: `SelectionToolbar` — the persistent bulk-action band

**Files:**
- Create: `src/frontend/src/modules/mail/list/SelectionToolbar.tsx`
- Test: `src/frontend/src/modules/mail/list/SelectionToolbar.test.tsx`
- Modify: the mail stylesheet (see Step 6)

**Interfaces:**
- Consumes: `DropdownMenu` + `type MenuEntry` (`src/components/DropdownMenu`), icons `ArchiveIcon`/`TrashIcon` and (new use) a junk + move icon — reuse `JunkIcon` and a move/arrow icon already in `src/icons/` (check `src/icons/` for the ones ReaderActions/the kebab use; if a "move" glyph is absent, reuse the same one the reader's Move entry pairs with — otherwise add none, the toolbar entries may be icon+label buttons using existing icons).
- Produces: `SelectionToolbar` (default export) with the props below. The component is dumb: it renders state and calls handlers; all role/enablement logic is computed by `MessageList` (Task 6) and passed in.

```tsx
export interface ToolbarAction {
  onRun: () => void
  disabledReason?: string   // undefined = enabled; string = disabled + title
}
export interface SelectionToolbarProps {
  title: string             // folder label, shown when nothing is selected
  count: number             // effective selected count
  allSelected: boolean
  indeterminate: boolean
  onToggleAll: () => void
  overCap: boolean          // > 200 selected → selection actions disabled with a cap tooltip
  deleteLabel: string       // 'Delete' | 'Delete permanently'
  archive: ToolbarAction
  junk: ToolbarAction
  del: ToolbarAction
  move: ToolbarAction
  copy: ToolbarAction
  markRead: ToolbarAction
  markUnread: ToolbarAction
  emptyFolder: ToolbarAction   // folder-level: enabled independently of the selection
}
```

- [ ] **Step 1: Write the failing test**

Create `SelectionToolbar.test.tsx`. Cover: title vs count, master checkbox state (checked/indeterminate) and `onToggleAll`, direct actions disabled when count 0, enabled when >0, over-cap disabling with the tooltip, and the kebab holding Mark read/unread + Copy + Empty folder with Empty folder enabled even at count 0.

```tsx
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent, within } from '@testing-library/react'
import SelectionToolbar, { type SelectionToolbarProps } from './SelectionToolbar'

const noop = { onRun: vi.fn() }
function props(over: Partial<SelectionToolbarProps> = {}): SelectionToolbarProps {
  return {
    title: 'Inbox', count: 0, allSelected: false, indeterminate: false, onToggleAll: vi.fn(),
    overCap: false, deleteLabel: 'Delete',
    archive: { ...noop }, junk: { ...noop }, del: { ...noop }, move: { ...noop }, copy: { ...noop },
    markRead: { ...noop }, markUnread: { ...noop }, emptyFolder: { ...noop }, ...over,
  }
}

describe('SelectionToolbar', () => {
  it('shows the folder title when nothing is selected, the count otherwise', () => {
    const { rerender } = render(<SelectionToolbar {...props()} />)
    expect(screen.getByText('Inbox')).toBeInTheDocument()
    rerender(<SelectionToolbar {...props({ count: 3 })} />)
    expect(screen.getByText('3 selected')).toBeInTheDocument()
  })

  it('greys the direct actions with an empty selection', () => {
    render(<SelectionToolbar {...props({ count: 0 })} />)
    expect(screen.getByRole('button', { name: 'Archive' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Delete' })).toBeDisabled()
  })

  it('enables the direct actions and fires them when a selection exists', () => {
    const archive = { onRun: vi.fn() }
    render(<SelectionToolbar {...props({ count: 2, archive })} />)
    const btn = screen.getByRole('button', { name: 'Archive' })
    expect(btn).toBeEnabled()
    fireEvent.click(btn)
    expect(archive.onRun).toHaveBeenCalledOnce()
  })

  it('disables selection actions over the 200 cap with a tooltip', () => {
    render(<SelectionToolbar {...props({ count: 201, overCap: true })} />)
    const btn = screen.getByRole('button', { name: 'Archive' })
    expect(btn).toBeDisabled()
    expect(btn).toHaveAttribute('title', 'Select 200 or fewer')
  })

  it('drives the master checkbox from allSelected/indeterminate', () => {
    const onToggleAll = vi.fn()
    render(<SelectionToolbar {...props({ count: 5, allSelected: true, onToggleAll })} />)
    const master = screen.getByRole('checkbox', { name: 'Select all' })
    expect(master).toBeChecked()
    fireEvent.click(master)
    expect(onToggleAll).toHaveBeenCalledOnce()
  })

  it('keeps Empty folder in the kebab enabled with no selection', () => {
    const emptyFolder = { onRun: vi.fn() }
    render(<SelectionToolbar {...props({ count: 0, emptyFolder })} />)
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    const item = screen.getByRole('menuitem', { name: 'Empty folder' })
    expect(item).toBeEnabled()
    fireEvent.click(item)
    expect(emptyFolder.onRun).toHaveBeenCalledOnce()
  })

  it('greys the selection-bound kebab items with no selection', () => {
    render(<SelectionToolbar {...props({ count: 0 })} />)
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    expect(screen.getByRole('menuitem', { name: 'Mark as read' })).toBeDisabled()
    expect(screen.getByRole('menuitem', { name: 'Copy to…' })).toBeDisabled()
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/frontend && npx vitest run src/modules/mail/list/SelectionToolbar.test.tsx`
Expected: FAIL — `SelectionToolbar` does not exist.

- [ ] **Step 3: Implement the component**

Create `SelectionToolbar.tsx`. The master checkbox needs its `indeterminate` set via a ref (React has no `indeterminate` prop). Direct-action `disabled` = `overCap || count === 0 || !!action.disabledReason`; its `title` prefers the cap message, then the role reason. Kebab uses `DropdownMenu`.

```tsx
import { useEffect, useRef } from 'react'
import DropdownMenu, { type MenuEntry } from '../../../components/DropdownMenu'
import ArchiveIcon from '../../../icons/ArchiveIcon'
import JunkIcon from '../../../icons/JunkIcon'
import TrashIcon from '../../../icons/TrashIcon'
import MoveIcon from '../../../icons/MoveIcon'

export interface ToolbarAction {
  onRun: () => void
  disabledReason?: string
}
export interface SelectionToolbarProps {
  title: string
  count: number
  allSelected: boolean
  indeterminate: boolean
  onToggleAll: () => void
  overCap: boolean
  deleteLabel: string
  archive: ToolbarAction
  junk: ToolbarAction
  del: ToolbarAction
  move: ToolbarAction
  copy: ToolbarAction
  markRead: ToolbarAction
  markUnread: ToolbarAction
  emptyFolder: ToolbarAction
}

const CAP = 'Select 200 or fewer'

export default function SelectionToolbar(props: SelectionToolbarProps) {
  const { title, count, allSelected, indeterminate, onToggleAll, overCap, deleteLabel } = props
  const master = useRef<HTMLInputElement>(null)
  useEffect(() => { if (master.current) master.current.indeterminate = indeterminate }, [indeterminate])

  // A selection action is off when nothing is selected, over the cap, or its role forbids it.
  // The cap message wins the tooltip, then the role reason.
  function actionProps(action: ToolbarAction) {
    const disabled = count === 0 || overCap || !!action.disabledReason
    const title = overCap ? CAP : action.disabledReason
    return { disabled, title, onClick: action.onRun }
  }

  const kebab: MenuEntry[] = [
    { label: 'Mark as read', onSelect: props.markRead.onRun, disabled: count === 0 || overCap },
    { label: 'Mark as unread', onSelect: props.markUnread.onRun, disabled: count === 0 || overCap },
    { label: 'Copy to…', onSelect: props.copy.onRun, disabled: count === 0 || overCap,
      title: overCap ? CAP : undefined },
    'separator',
    { label: 'Empty folder', onSelect: props.emptyFolder.onRun,
      disabled: !!props.emptyFolder.disabledReason, title: props.emptyFolder.disabledReason },
  ]

  return (
    <div className="selection-toolbar">
      <input
        ref={master}
        type="checkbox"
        className="selection-master"
        aria-label="Select all"
        checked={allSelected}
        onChange={onToggleAll}
      />
      <span className="selection-title">{count > 0 ? `${count} selected` : title}</span>
      <div className="selection-actions">
        <button type="button" className="row-btn" aria-label="Archive" {...actionProps(props.archive)}>
          <ArchiveIcon size={16} />
        </button>
        <button type="button" className="row-btn" aria-label="Report as junk" {...actionProps(props.junk)}>
          <JunkIcon size={16} />
        </button>
        <button type="button" className="row-btn" aria-label={deleteLabel} {...actionProps(props.del)}>
          <TrashIcon size={16} />
        </button>
        <button type="button" className="row-btn" aria-label="Move to…" {...actionProps(props.move)}>
          <MoveIcon size={16} />
        </button>
        <DropdownMenu ariaLabel="More actions" className="row-btn" trigger={<span aria-hidden>⋯</span>} items={kebab} />
      </div>
    </div>
  )
}
```

Note on icons: confirm `JunkIcon` and a move glyph exist under `src/icons/`. If `MoveIcon` is absent, use the icon the reader's "Move to…" entry uses, or the closest existing arrow/folder icon — do not invent a new SVG in this task; reuse an existing one and record the choice in the report.

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/frontend && npx vitest run src/modules/mail/list/SelectionToolbar.test.tsx`
Expected: PASS. If the `DropdownMenu` trigger's accessible name differs, align the `getByRole('button', { name: 'More actions' })` query with the real `ariaLabel`.

- [ ] **Step 5: Break-verify the cap tooltip**

Temporarily drop `overCap` from `actionProps` (`const disabled = count === 0 || !!action.disabledReason`); rerun — the over-cap test must fail (Archive enabled). Restore.

- [ ] **Step 6: Style the toolbar (measure in a real browser)**

Add `.selection-toolbar` styles to the mail stylesheet, replacing what `.message-list-heading` provided (it is the same band). Flex row: master checkbox, title (`flex: 1`, ellipsised), then `.selection-actions` (the four `row-btn` + kebab). Use tokens only. **Open the app in a browser** and confirm the band matches the old heading height and that the four buttons + kebab do not wrap or clip in the 240px narrow column.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/modules/mail/list/SelectionToolbar.tsx src/frontend/src/modules/mail/list/SelectionToolbar.test.tsx src/frontend/src/styles/
git commit -F - <<'EOF'
Frontend 2b3: SelectionToolbar band

Master checkbox + count/title + role-aware Archive/Junk/Delete/Move and a
kebab (mark read/unread, copy, empty folder). Dumb component, driven by props.
EOF
```

---

## Task 5: `EmptyFolderBanner` — the pinned trash/junk shortcut

**Files:**
- Create: `src/frontend/src/modules/mail/list/EmptyFolderBanner.tsx`
- Test: `src/frontend/src/modules/mail/list/EmptyFolderBanner.test.tsx`
- Modify: the mail stylesheet

**Interfaces:**
- Consumes: `TrashIcon`, `SpecialUse` (`../api/mailTypes`).
- Produces: `EmptyFolderBanner` (default export), props `{ role: SpecialUse | null; total: number; onEmpty: () => void }`. Renders **only** when `role === 'trash' || role === 'junk'` **and** `total > 0`; otherwise returns `null`. The copy describes the action, never server retention.

- [ ] **Step 1: Write the failing test**

Create `EmptyFolderBanner.test.tsx`:

```tsx
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import EmptyFolderBanner from './EmptyFolderBanner'

describe('EmptyFolderBanner', () => {
  it('renders trash copy and fires onEmpty from the link', () => {
    const onEmpty = vi.fn()
    render(<EmptyFolderBanner role="trash" total={4} onEmpty={onEmpty} />)
    expect(screen.getByText('Emptying the trash permanently deletes these messages.')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Empty trash now' }))
    expect(onEmpty).toHaveBeenCalledOnce()
  })

  it('renders junk copy', () => {
    render(<EmptyFolderBanner role="junk" total={2} onEmpty={vi.fn()} />)
    expect(screen.getByText('Emptying the junk folder permanently deletes these messages.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Empty junk now' })).toBeInTheDocument()
  })

  it('renders nothing outside trash/junk', () => {
    const { container } = render(<EmptyFolderBanner role="archive" total={9} onEmpty={vi.fn()} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('renders nothing when the folder is empty', () => {
    const { container } = render(<EmptyFolderBanner role="trash" total={0} onEmpty={vi.fn()} />)
    expect(container).toBeEmptyDOMElement()
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/frontend && npx vitest run src/modules/mail/list/EmptyFolderBanner.test.tsx`
Expected: FAIL — component does not exist.

- [ ] **Step 3: Implement the banner**

Create `EmptyFolderBanner.tsx`:

```tsx
import type { SpecialUse } from '../api/mailTypes'
import TrashIcon from '../../../icons/TrashIcon'

interface Props {
  role: SpecialUse | null
  total: number
  onEmpty: () => void
}

// The copy describes the effect of the action, never the server's retention: some servers purge
// the trash after N days on their own — we do not control that, so we never assert it.
const COPY: Record<string, { text: string; link: string }> = {
  trash: { text: 'Emptying the trash permanently deletes these messages.', link: 'Empty trash now' },
  junk: { text: 'Emptying the junk folder permanently deletes these messages.', link: 'Empty junk now' },
}

export default function EmptyFolderBanner({ role, total, onEmpty }: Props) {
  const copy = role ? COPY[role] : undefined
  if (!copy || total <= 0) return null

  return (
    <div className="empty-folder-banner">
      <TrashIcon size={16} />
      <span className="empty-folder-banner-text">{copy.text}</span>
      <button type="button" className="empty-folder-banner-link" onClick={onEmpty}>{copy.link}</button>
    </div>
  )
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/frontend && npx vitest run src/modules/mail/list/EmptyFolderBanner.test.tsx`
Expected: PASS (all four cases).

- [ ] **Step 5: Style the banner**

Add `.empty-folder-banner` (a thin `flex: none` strip: icon, text `flex: 1`, right-aligned link), `.empty-folder-banner-link` (a text-button, danger accent on hover, tokens only) to the mail stylesheet. **Confirm in a browser** it sits between the toolbar and the scroll area and stays put while the rows scroll.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/modules/mail/list/EmptyFolderBanner.tsx src/frontend/src/modules/mail/list/EmptyFolderBanner.test.tsx src/frontend/src/styles/
git commit -F - <<'EOF'
Frontend 2b3: EmptyFolderBanner

Pinned trash/junk shortcut; action-worded copy (no retention claim), fires
onEmpty. Renders only for trash/junk with total > 0.
EOF
```

---

## Task 6: Wire selection + bulk actions into `MessageList`

**Files:**
- Modify: `src/frontend/src/modules/mail/list/MessageList.tsx`
- Test: `src/frontend/src/modules/mail/list/MessageList.test.tsx`

**Interfaces:**
- Consumes: `useSelection` (Task 3), `SelectionToolbar` + `type ToolbarAction` (Task 4), existing `useSetFlags`/`useMoveMessages`/`useDeleteMessages`, `rolePathsOf`, `nextUidOf`, `DeleteConfirmModal`.
- Produces: nothing new for later tasks; Task 7 adds the empty-folder wiring on top of this file.

Design notes for the implementer:
- **Effective selection:** `const selectedUids = messages.filter(m => selection.has(m.uid)).map(m => m.uid)`; `count = selectedUids.length`; `allSelected = count > 0 && count === messages.length`; `indeterminate = count > 0 && !allSelected`; `overCap = count > 200`.
- **resetKey:** `` `${folderPath}::${paging ? paging.page : 'stream'}` `` — clears the selection on folder change and (paged) page change, but not while streaming more blocks.
- **Row checkbox:** leftmost in both skins, a `row-btn`-style checkbox whose `onClick` does `event.stopPropagation()` (so it never opens the message) and calls `selection.toggle(uid, index)` normally or `selection.toggleRange(messages.map(m => m.uid), index)` when `event.shiftKey`. `aria-label={`Select message from ${from}`}`.
- **Master toggle:** `onToggleAll = () => allSelected ? selection.clear() : selection.selectAll(messages.map(m => m.uid))`.
- **Bulk handlers** (all guard `folderPath` and `selectedUids.length`, then clear the selection and advance the reader if the open row departs):
  - archive → `moveMessages.mutate({ folderPath, uids: selectedUids, targetFolderPath: roles.archive, copy: false })`
  - junk → same to `roles.junk`
  - move/copy → open `MoveMessagesModal` with `selectedUids` (reuse the existing picker already wired for the row/reader; pass the batch and `copy` flag)
  - delete → outside trash: move to `roles.trash`; inside trash: open the plural `DeleteConfirmModal`, and on confirm `deleteMessages.mutate({ folderPath, uids: selectedUids })`
  - markRead/markUnread → `setFlags.mutate({ folderPath, uids: selectedUids, flag: 'seen', value: true|false })`
- **Reader advance:** after a bulk action whose `selectedUids` includes `selectedUid`, call `onDeparted?.(selectedUid)` once. Extract a helper `runBulk(uids, fire)` that fires the mutation, calls `onDeparted` when `selectedUid` is in `uids`, then `selection.clear()`.
- **Role reasons** reuse the existing `inTrash`/`archiveOff`/`archiveReason`/`trashOff`/`deleteLabel` values already computed in the file; add a `junkOff`/`junkReason` mirroring archive (disabled inside the junk folder, or when `!roles.junk`).
- **Escape:** a keydown handler on the scroll container (or list root) that, when a selection is active, clears it and stops propagation — so it does not also reach any list-level handler. The reader's Escape is bound only in `none` mode where the list is hidden, so there is no conflict.

- [ ] **Step 1: Write the failing tests**

Add a `describe('multi-select', …)` block to `MessageList.test.tsx`. Use the existing `roleTree`/`renderList` helpers (set `mocks.folders` to a tree carrying trash/archive/junk roles so `rolePathsOf` resolves; the existing "row controls" block shows the pattern). Cover the behaviours below.

```tsx
// inside MessageList.test.tsx, a new describe block
describe('multi-select', () => {
  // Assumes a helper that renders with folders carrying roles; mirror the existing
  // role-aware suite's setup (mocks.folders = roleTree).
  it('checking rows shows the count and enables the direct actions', async () => {
    renderWithRoles() // existing helper pattern: sets mocks.folders + renders INBOX
    fireEvent.click(screen.getByRole('checkbox', { name: /select message from alice/i }))
    expect(screen.getByText('1 selected')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Archive' })).toBeEnabled()
  })

  it('a shift-click selects the range', async () => {
    renderWithRoles()
    const boxes = screen.getAllByRole('checkbox', { name: /select message from/i })
    fireEvent.click(boxes[0])
    fireEvent.click(boxes[1], { shiftKey: true })
    expect(screen.getByText('2 selected')).toBeInTheDocument()
  })

  it('the master checkbox selects and clears all loaded rows', async () => {
    renderWithRoles()
    const master = screen.getByRole('checkbox', { name: 'Select all' })
    fireEvent.click(master)
    expect(screen.getByText('2 selected')).toBeInTheDocument()
    fireEvent.click(master)
    expect(screen.getByText('Inbox')).toBeInTheDocument() // title back, selection cleared
  })

  it('bulk archive moves the whole selection and clears it', async () => {
    renderWithRoles()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select all' }))
    fireEvent.click(screen.getByRole('button', { name: 'Archive' }))
    expect(mocks.move).toHaveBeenCalledWith(expect.objectContaining({
      folderPath: 'INBOX', uids: [2, 1], targetFolderPath: expect.any(String), copy: false,
    }))
    await settle()
    expect(screen.getByText('Inbox')).toBeInTheDocument() // selection cleared
  })

  it('bulk delete inside the trash asks for confirmation, then expunges the batch', async () => {
    renderWithRoles('trash') // render the trash folder
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select all' }))
    fireEvent.click(screen.getByRole('button', { name: 'Delete permanently' }))
    expect(mocks.remove).not.toHaveBeenCalled()
    await settle()
    fireEvent.click(screen.getByRole('button', { name: 'Delete' })) // confirm modal
    expect(mocks.remove).toHaveBeenCalledWith(expect.objectContaining({ folderPath: 'Trash', uids: [2, 1] }))
  })

  it('advances the reader when the open message is in the acted batch', async () => {
    const onDeparted = vi.fn()
    renderWithRoles(undefined, { selectedUid: 2, onDeparted })
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select all' }))
    fireEvent.click(screen.getByRole('button', { name: 'Archive' }))
    expect(onDeparted).toHaveBeenCalledWith(2)
  })

  it('does not advance the reader when the open message is untouched', async () => {
    const onDeparted = vi.fn()
    renderWithRoles(undefined, { selectedUid: 999, onDeparted })
    fireEvent.click(screen.getByRole('checkbox', { name: /select message from alice/i }))
    fireEvent.click(screen.getByRole('button', { name: 'Archive' }))
    await settle()
    expect(onDeparted).not.toHaveBeenCalled()
  })

  it('clears the selection when the folder changes', async () => {
    const { rerender } = renderWithRoles()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select all' }))
    expect(screen.getByText('2 selected')).toBeInTheDocument()
    rerender(/* same tree, folderPath="Archive" */)
    expect(screen.queryByText('2 selected')).not.toBeInTheDocument()
  })

  it('Escape clears an active selection', async () => {
    renderWithRoles()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select all' }))
    fireEvent.keyDown(screen.getByRole('checkbox', { name: 'Select all' }), { key: 'Escape' })
    expect(screen.getByText('Inbox')).toBeInTheDocument()
  })
})
```

The `renderWithRoles(role?, props?)` helper: reuse/extend the role-aware setup already in the file (the block that sets `mocks.folders = roleTree`). If none is factored out, add a small local helper there that sets `mocks.folders` to a tree with `inbox/archive/junk/trash` roles and renders `folderPath` = INBOX (or the trash path) with `folderRole` set accordingly. Keep the two `sample` rows (uids 2, 1).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/frontend && npx vitest run src/modules/mail/list/MessageList.test.tsx -t multi-select`
Expected: FAIL — no checkboxes, no `SelectionToolbar`.

- [ ] **Step 3: Implement the wiring**

In `MessageList.tsx`:
1. Import `useSelection`, `SelectionToolbar`, `type ToolbarAction`.
2. Compute `resetKey` and call `const selection = useSelection(resetKey)`.
3. Derive `selectedUids`, `count`, `allSelected`, `indeterminate`, `overCap` (see design notes).
4. Add `junkOff`/`junkReason` next to the existing role values.
5. Add `function runBulk(uids: number[], fire: () => void)` that calls `fire()`, then `if (selectedUid !== null && uids.includes(selectedUid)) onDeparted?.(selectedUid)`, then `selection.clear()`.
6. Build the eight `ToolbarAction`s and render `<SelectionToolbar …/>` in place of the `<h2 className="message-list-heading">…</h2>` (keep the same outer band position — the toolbar *is* the heading band now). Pass `title={folderName || folderPath}`.
7. Add the leftmost row checkbox to both skins with `stopPropagation` + shift handling.
8. Add the Escape handler.
9. Keep `expunging`/`DeleteConfirmModal` for a single in-trash row; for the bulk in-trash delete, drive the same `DeleteConfirmModal` with a plural label — reuse the modal, tracking a `confirmingBulk` boolean, and on confirm call `runBulk(selectedUids, () => deleteMessages.mutate({ folderPath, uids: selectedUids }))`.

Leave the empty-folder wiring (`emptyFolder` action target + banner) as a **stub** for Task 7: pass `emptyFolder={{ onRun: () => {}, disabledReason: undefined }}` for now so the toolbar renders; Task 7 replaces it. (Do not render `EmptyFolderBanner` yet.)

Reference for the row checkbox (two-line skin shown; mirror in the wide skin, leftmost):

```tsx
<input
  type="checkbox"
  className="message-row-check"
  aria-label={`Select message from ${from}`}
  checked={selection.has(message.uid)}
  onClick={event => {
    event.stopPropagation()
    if ((event as unknown as MouseEvent).shiftKey) selection.toggleRange(messages.map(m => m.uid), index)
    else selection.toggle(message.uid, index)
  }}
  onChange={() => {}}
/>
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd src/frontend && npx vitest run src/modules/mail/list/MessageList.test.tsx`
Expected: PASS — the new `multi-select` block and all pre-existing MessageList tests (the tooltip/role-control tests still hold, now reading the toolbar's buttons where relevant).

- [ ] **Step 5: Break-verify the reader-advance guard**

Temporarily change the guard to always call `onDeparted?.(selectedUid!)`; rerun — "does not advance the reader when the open message is untouched" must fail. Restore.

- [ ] **Step 6: Style the row checkbox + measure**

Add `.message-row-check` (leftmost, revealed on `:hover`/`:focus-within` of the row and always shown once `.message-row` is within a list that has a selection — a body class or a `data-` attr on the list, or simply always-visible-on-hover plus a `.has-selection` modifier on `.message-list`). **Measure in a real browser**: both skins, narrow (240px) and wide, at rest and hover — the checkbox must not shove the sender/subject or change the 42px wide-row height. Record the measured height in the report.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/modules/mail/list/MessageList.tsx src/frontend/src/modules/mail/list/MessageList.test.tsx src/frontend/src/styles/
git commit -F - <<'EOF'
Frontend 2b3: multi-select + bulk actions in MessageList

Row checkboxes (both skins, shift-range), SelectionToolbar replaces the
heading, bulk archive/junk/delete/move/copy/mark reusing 2b2 hooks; reader
advances when the open row departs; Escape and folder/page change clear.
EOF
```

---

## Task 7: Wire "empty folder" into `MessageList` (kebab + banner)

**Files:**
- Modify: `src/frontend/src/modules/mail/list/MessageList.tsx`
- Test: `src/frontend/src/modules/mail/list/MessageList.test.tsx`

**Interfaces:**
- Consumes: `useEmptyFolder` (Task 2), `EmptyFolderBanner` (Task 5), `DeleteConfirmModal`, `rolePathsOf`.

Design notes:
- **Mode decision (frontend, mirroring 2b2 Delete):** in trash/junk → **purge** (`useEmptyFolder.mutate({ folderPath })`, no target); elsewhere → **move to trash** (`{ folderPath, targetFolderPath: roles.trash }`).
- **Confirm:** a permanent purge (trash **or** junk) goes through `DeleteConfirmModal` first; the move-to-trash case fires with no confirm. Track a `confirmingEmpty` boolean; the modal's `entityLabel` is the folder label.
- **Enablement:** the kebab "Empty folder" `disabledReason` = `'This folder is already empty'` when `total === 0`; `'Assign the trash folder in Settings → Folders'` when a normal folder needs the trash role but `!roles.trash`; otherwise enabled.
- **Banner:** render `<EmptyFolderBanner role={folderRole} total={total} onEmpty={requestEmpty} />` between the toolbar and `.mail-list-scroll`. Its `onEmpty` runs the same `requestEmpty()` the kebab uses (for trash/junk this always means purge + confirm).
- **One code path:** a single `requestEmpty()` decides purge vs move and whether to confirm; the kebab and the banner both call it.

- [ ] **Step 1: Write the failing tests**

Add to the `multi-select` (or a new `empty-folder`) describe block:

```tsx
describe('empty folder', () => {
  it('shows the trash banner and purges after confirmation', async () => {
    renderWithRoles('trash')
    fireEvent.click(screen.getByRole('button', { name: 'Empty trash now' }))
    expect(mocks.empty).not.toHaveBeenCalled()          // confirm first
    await settle()
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }))
    expect(mocks.empty).toHaveBeenCalledWith({ folderPath: 'Trash' })
  })

  it('no banner outside trash/junk', () => {
    renderWithRoles() // INBOX
    expect(screen.queryByRole('button', { name: /empty .* now/i })).not.toBeInTheDocument()
  })

  it('kebab Empty folder on a normal folder moves everything to trash, no confirm', async () => {
    renderWithRoles() // INBOX
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Empty folder' }))
    expect(mocks.empty).toHaveBeenCalledWith({ folderPath: 'INBOX', targetFolderPath: expect.any(String) })
  })

  it('disables Empty folder when the folder is empty', () => {
    renderWithRoles(undefined, {}, { total: 0, messages: [] })
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    expect(screen.getByRole('menuitem', { name: 'Empty folder' })).toBeDisabled()
  })
})
```

Extend the `queries` mock in this file to expose `useEmptyFolder`: add `useEmptyFolder: () => ({ mutate: mocks.empty })` to the `vi.mock('../queries', …)` factory, and `empty: vi.fn()` to the hoisted `mocks`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/frontend && npx vitest run src/modules/mail/list/MessageList.test.tsx -t "empty folder"`
Expected: FAIL — no banner, kebab Empty folder is a no-op stub.

- [ ] **Step 3: Implement the wiring**

In `MessageList.tsx`:
1. Import `useEmptyFolder` and `EmptyFolderBanner`.
2. `const emptyFolder = useEmptyFolder(onNotify)`.
3. Add `const [confirmingEmpty, setConfirmingEmpty] = useState(false)`.
4. `const purges = folderRole === 'trash' || folderRole === 'junk'`.
5. `function requestEmpty()` → if `purges` set `setConfirmingEmpty(true)`; else `emptyFolder.mutate({ folderPath, targetFolderPath: roles.trash })`.
6. `function confirmEmpty()` → `emptyFolder.mutate({ folderPath }); setConfirmingEmpty(false)`.
7. Replace the Task 6 stub with `emptyFolder={{ onRun: requestEmpty, disabledReason: emptyReason }}` where `emptyReason` = `total === 0 ? 'This folder is already empty' : (!purges && !roles.trash ? 'Assign the trash folder in Settings → Folders' : undefined)`.
8. Render `<EmptyFolderBanner role={folderRole ?? null} total={total} onEmpty={requestEmpty} />` right after `<SelectionToolbar/>`, before `.mail-list-scroll`.
9. Render the purge confirm modal when `confirmingEmpty`: `<DeleteConfirmModal entityLabel={folderName || folderPath} onConfirm={confirmEmpty} onClose={() => setConfirmingEmpty(false)} loading={emptyFolder.isPending} />`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd src/frontend && npx vitest run src/modules/mail/list/MessageList.test.tsx`
Expected: PASS — the new `empty folder` cases and the whole existing file.

- [ ] **Step 5: Break-verify the purge confirm gate**

Temporarily make `requestEmpty` call `emptyFolder.mutate({ folderPath })` directly in the `purges` branch (skipping the modal); rerun — "shows the trash banner and purges after confirmation" must fail on the pre-confirm `not.toHaveBeenCalled()`. Restore.

- [ ] **Step 6: Style + browser check**

Confirm in a browser: the banner shows in trash and junk (non-empty), the kebab "Empty folder" is present in every folder, the purge confirm appears for trash/junk, and a normal-folder empty moves to trash with no confirm.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/modules/mail/list/MessageList.tsx src/frontend/src/modules/mail/list/MessageList.test.tsx
git commit -F - <<'EOF'
Frontend 2b3: empty folder wiring (kebab + banner)

One requestEmpty path: purge (trash/junk, confirmed) or move-all-to-trash
(elsewhere, no confirm). Pinned banner in trash/junk drives the same path.
EOF
```

---

## Task 8: Docs + full gates

**Files:**
- Modify: `src/frontend/CLAUDE.md`, `src/snoopy.microservice/CLAUDE.md`
- Modify (regenerate): `src/snoopy.microservice/ApiDocumentation.xml`

- [ ] **Step 1: Update the backend CLAUDE.md**

In the `MailController` bullet, add the empty endpoint next to Move/Copy/Delete: `POST /api/Mail/Folders/Empty` (body `{ folderPath, targetFolderPath? }`) — purge when no target (mark 1:* `\Deleted` + `EXPUNGE`, no UIDPLUS needed), move-all when a target is given; 204/400/401/502.

- [ ] **Step 2: Update the frontend CLAUDE.md**

- Update the Project paragraph: mail now supports **multi-select** (checkbox selection with a bulk-action toolbar) and **emptying a folder** (purge trash/junk, move-all elsewhere).
- Under `list/`, add `SelectionToolbar.tsx`, `EmptyFolderBanner.tsx`, `useSelection.ts`, and note `MessageList` owns the selection + bulk actions and that the heading band is now the toolbar.
- Under `queries.ts`, note `useEmptyFolder` carries `mailKeys.writes` and patches counts from the source tree node.

- [ ] **Step 3: Regenerate the API documentation**

Build the microservice so the XML doc regenerates (or run the project's doc step), then stage the updated `ApiDocumentation.xml`.

Run: `cd src/snoopy.microservice && dotnet build -c Release`
Expected: build succeeds; `ApiDocumentation.xml` includes the `EmptyFolder` summary.

- [ ] **Step 4: Run all gates**

```bash
cd src/snoopy.microservice && dotnet test
cd ../frontend && npx vitest run && npm run build && npx eslint . && npx tsc --noEmit
```
Expected: backend green; frontend all tests pass; build clean; eslint 0 errors (the 3 pre-existing admin-tab warnings are acceptable); tsc clean.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/CLAUDE.md src/snoopy.microservice/CLAUDE.md src/snoopy.microservice/ApiDocumentation.xml
git commit -F - <<'EOF'
Docs 2b3: multi-select, bulk actions, empty folder

True up both CLAUDE.md files and regenerate ApiDocumentation.xml for the new
empty-folder endpoint.
EOF
```

---

## Self-Review

**Spec coverage:**
- §1 place / data layer already batches → Tasks 6/7 reuse 2b2 hooks; §1 selection independent of open message → Task 6 (checkbox `stopPropagation`, reader independence).
- §2 checkbox model, hover/persistent, click-to-open, shift-range → Task 3 (hook) + Task 6 (row checkbox, both skins).
- §3 toolbar: master + indeterminate, title↔count, role-aware Archive/Junk/Delete/Move, kebab (mark read/unread, copy, empty) → Task 4 + Task 6 wiring.
- §4 bulk actions reuse hooks; reader advance; selection cleared after action → Task 6.
- §5 empty folder: purge trash/junk vs move-all; confirm on purge (trash+junk); disabled when empty; backend endpoint; cache patch from tree node; `mailKeys.writes` → Tasks 1, 2, 7.
- §6 pinned trash/junk banner, action-worded copy → Task 5 + Task 7.
- §7 clear on folder+page, stream persistence, >200 disable, Escape → Tasks 3 (reset), 4 (overCap), 6 (Escape, resetKey).
- §8 file decomposition → Tasks 2–7 match the spec's file list.
- §9 global constraints → carried in the header + each task's steps.
- §10 YAGNI (no select-all-in-folder, no bulk star, no DnD) → not built.

**Placeholder scan:** No TBD/TODO. Two deliberate implementer judgment calls are flagged, not hidden: the move/junk **icon** choice in Task 4 Step 3 (reuse an existing icon, record which) and the `renderWithRoles` **test helper** in Task 6 (extend the file's existing role-aware setup). Both are bounded and named.

**Type consistency:** `EmptyFolderArgs { folderPath, targetFolderPath? }` (Task 2) matches `useEmptyFolder.mutate` calls (Task 7). `EmptyAsync(folderPath, targetPath?, ct)` consistent across session/repository/controller (Task 1). `ToolbarAction { onRun, disabledReason? }` consistent between Task 4's props and Task 6's construction. `useSelection` surface (Task 3) matches its use in Task 6. `emptyFolder(folder, targetFolder)` api ↔ `api.emptyFolder(folderPath, targetFolderPath ?? null)` (Task 2).
