# Tranche 2b2 — Actions de message — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Trash, archive, junk, move/copy and permanent delete, from the row cluster, the reader header and the kebab — with the filterable folder picker and next-message advance.

**Architecture:** Three endpoints (`POST Move`, `POST Copy`, `DELETE Messages`) flow Controller → repository → `ImapSession` (MailKit `MoveToAsync`/`CopyToAsync`; permanent delete is `\Deleted` + `UID EXPUNGE`, refused without UIDPLUS). "Delete" in daily use is not an endpoint: the frontend moves to the `trash` role. Two optimistic mutations remove rows from source caches, drop the target folder's cached pages, and patch both folders' counters; all three write mutations share one mutation key so the poll stands down for any of them.

**Tech Stack:** ASP.NET Core (.NET 10) + MailKit + xUnit/Moq; React + TanStack Query + Vitest/Testing Library.

**Spec:** `docs/superpowers/specs/2026-07-22-webmail-actions-2b2-design.md`

## Global Constraints

- Commit messages: concise, **two lines max**, with a real newline between subject and body.
- Code comments only where the code cannot say it itself; **3 lines max**.
- UI copy English; tokens only, never a colour literal; six palettes + parity test.
- **Never `invalidateQueries` on the messageStream key.** Target-folder caches are **removed** (`removeQueries`), not invalidated — removal refetches nothing until the folder is shown.
- Folder paths in body/query, never in a route segment.
- Backend: `dotnet test` (never `--no-build`). `FromResult(Result, …, successStatusCode: 204)` returns **`StatusCodeResult`** — assert `Assert.IsType<StatusCodeResult>` + status 204, the `SetFolderSubscription`/`SetMessageFlags` twin shape (known plan erratum from 2b1, do not reintroduce).
- Frontend negative assertions: TanStack notifies on a macrotask — put `await settle()` (from `src/test-utils.ts`) before every silence assertion. A silence test without it passes against any implementation (bit twice in 2b1).
- Where a test guards something subtle, verify by breaking the implementation and watching it fail.
- CSS layout claims (row height, reserve widths) are **measured in a real browser**, never assumed — jsdom does no layout. 2b1's probe technique: a standalone page served by Vite loading the real stylesheets.
- Batch ceilings: uids 1–200 everywhere.
- Tests sit next to what they test. Working dirs: backend `src/snoopy.microservice`, frontend `src/frontend`.

---

### Task 1: Backend — session move/copy + repository

**Files:**
- Modify: `src/snoopy.microservice/Services/IImapSession.cs`
- Modify: `src/snoopy.microservice/Services/ImapSession.cs` (after `SetFlagsAsync`, ~line 260)
- Modify: `src/snoopy.microservice/Repositories/IMailMessageRepository.cs`
- Modify: `src/snoopy.microservice/Repositories/MailMessageRepository.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/MailMessageRepositoryTests.cs`

**Interfaces:**
- Produces: `ImapSession.TargetNotSelectable` (public const string, value `"target_not_selectable"`);
  `Task<Result> IImapSession.MoveOrCopyAsync(string folderPath, IReadOnlyList<uint> uids, string targetPath, bool copy, CancellationToken ct)`;
  same signature on `IMailMessageRepository` with `(User user, string password, …)` prefixed. One session method with a bool rather than two: the open/resolve/refuse body is identical and the spec's "méthode commune avec un booléen" covers it — the public twins live at the controller.

- [ ] **Step 1: Failing repository tests** — mirror the `SetFlagsAsync_*` group added in 2b1 (same `CreateSut()`, same `Alice` builder): `MoveOrCopyAsync_DelegatesToTheSession` (verify exact args incl. `copy: true` and `false` via `[Theory]`), `_PropagatesAConnectionFailure`, `_DisposesTheSession`, `_ThrowsWhenUserIsNull`.
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~MailMessageRepositoryTests"` — expected: compile errors (method absent).
- [ ] **Step 3: Implement.** Session:

```csharp
public const string TargetNotSelectable = "target_not_selectable";

public async Task<Result> MoveOrCopyAsync(string folderPath, IReadOnlyList<uint> uids, string targetPath, bool copy, CancellationToken cancellationToken)
{
    ThrowIfDisposed();

    try
    {
        IMailFolder target;
        try { target = await _client.GetFolderAsync(targetPath, cancellationToken); }
        catch (FolderNotFoundException) { return Result.Failure(TargetNotSelectable); }

        // A \NoSelect container cannot hold messages; refusing here beats a server error the
        // client cannot word. Checked by the session because the controller has no tree.
        if ((target.Attributes & (FolderAttributes.NoSelect | FolderAttributes.NonExistent)) != 0)
            return Result.Failure(TargetNotSelectable);

        var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

        var ids = uids.Select(uid => new UniqueId(uid)).ToList();
        // MailKit uses MOVE when advertised and falls back to COPY + \Deleted + EXPUNGE itself.
        if (copy) await folder.CopyToAsync(ids, target, cancellationToken);
        else await folder.MoveToAsync(ids, target, cancellationToken);

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
        _logger.LogError(ex, "Failed to {Verb} {Count} messages from {Folder} to {Target}",
            copy ? "copy" : "move", uids.Count, folderPath, targetPath);
        return Result.Failure(copy ? "Unable to copy the messages" : "Unable to move the messages");
    }
}
```

Repository: the exact shape of `SetFlagsAsync` (null-check, open, propagate, `await using`, delegate).
- [ ] **Step 4: Run** the filter again — PASS. **Step 5: Commit** (`Session and repository move/copy, target selectability refused by the session`).

---

### Task 2: Backend — session permanent delete + repository

**Files:** same four as Task 1 + the repository test file.

**Interfaces:**
- Produces: `Task<Result> IImapSession.DeleteAsync(string folderPath, IReadOnlyList<uint> uids, CancellationToken ct)`; repository mirror with user/password. Error constant not needed — the UIDPLUS message is a plain failure string the controller maps to 502: `"The mail server cannot delete single messages (no UIDPLUS)"`.

- [ ] **Step 1: Failing repository tests** — `DeleteAsync_DelegatesToTheSession`, `_PropagatesAConnectionFailure`, `_DisposesTheSession`, `_ThrowsWhenUserIsNull`.
- [ ] **Step 2: Run to fail.** **Step 3: Implement.** Session:

```csharp
public async Task<Result> DeleteAsync(string folderPath, IReadOnlyList<uint> uids, CancellationToken cancellationToken)
{
    ThrowIfDisposed();

    // A bare EXPUNGE purges every \Deleted message in the folder, including ones another
    // client marked and has not purged. UID EXPUNGE (UIDPLUS) limits it to ours — without
    // it, refusing beats widening the purge. Capabilities are read after authentication.
    if (!_client.Capabilities.HasFlag(ImapCapabilities.UidPlus))
        return Result.Failure("The mail server cannot delete single messages (no UIDPLUS)");

    try
    {
        var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

        var ids = uids.Select(uid => new UniqueId(uid)).ToList();
        await folder.AddFlagsAsync(ids, MessageFlags.Deleted, silent: true, cancellationToken);
        await folder.ExpungeAsync(ids, cancellationToken);

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
        _logger.LogError(ex, "Failed to expunge {Count} messages from {Folder}", uids.Count, folderPath);
        return Result.Failure("Unable to delete the messages");
    }
}
```

- [ ] **Step 4: PASS.** **Step 5: Commit** (`Permanent delete: \Deleted + UID EXPUNGE, refused without UIDPLUS`).

---

### Task 3: Backend — the three controller actions

**Files:**
- Modify: `src/snoopy.microservice/Models/Mail/MessageRequests.cs` (add two classes)
- Modify: `src/snoopy.microservice/Controllers/MailController.cs` (after `SetMessageFlags`)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs`

**Interfaces:**
- Produces: `MoveMessagesRequest { FolderPath, Uids, TargetFolderPath }` and `DeleteMessagesRequest { FolderPath, Uids }` (sealed classes, `FolderRequests.cs` style);
  `POST /api/Mail/Messages/Move`, `POST /api/Mail/Messages/Copy` (both `MoveMessagesRequest`), `DELETE /api/Mail/Messages` (`DeleteMessagesRequest`) — all 204/400/401/502.

- [ ] **Step 1: Failing controller tests**, twins of the `SetMessageFlags_*` group. Per verb: 204 + delegation with exact args (`copy` true/false per route); 400 blank source, blank target (Move/Copy), empty batch, > 200, **target == source** (`FolderPath = "INBOX", TargetFolderPath = "INBOX"`); 401; 502. Plus: `Move_Returns400WhenTheTargetIsNotSelectable` — repository answers `Result.Failure(ImapSession.TargetNotSelectable)` → `BadRequestObjectResult` (the `MessageNotFound` mapping pattern, inverted to 400). And `Delete_Returns502WithTheUidplusMessage` — failure string surfaces in the envelope.
- [ ] **Step 2: Run to fail.** **Step 3: Implement.** Move and Copy are eight-line twins delegating to a shared private `MoveOrCopy(request, copy, ct)`; validation order: source → uids count → target blank → target == source (ordinal compare) → credentials → repository. Map `TargetNotSelectable` to 400 with the message `"The target folder cannot hold messages"`. `FromResult(result, errorStatusCode: 502, successStatusCode: 204)`. Delete: same minus target checks. XML doc + `[ProducesResponseType]` per status, `<response>` tags — the house convention.
- [ ] **Step 4: `dotnet test`** — full suite PASS; commit regenerated `ApiDocumentation.xml`. **Step 5: Commit** (`Move, Copy and permanent Delete endpoints, target checks in order`).

---

### Task 4: Frontend — API client methods

**Files:** `src/frontend/src/api.js` (mail section), `src/frontend/src/api.test.js`.

**Interfaces:**
- Produces: `api.moveMessages(folder, uids, targetFolder)`, `api.copyMessages(folder, uids, targetFolder)` → `POST …/Move|Copy` body `{ folderPath, uids, targetFolderPath }`; `api.deleteMessages(folder, uids)` → `DELETE /api/Mail/Messages` body `{ folderPath, uids }`.

- [ ] **Steps 1–5:** one test per method in the file's real dialect (`mockFetch` + exact stringified body — the 2b1 shape), fail, implement beside `setMessageFlags` (paths in the **body**, unencoded), pass, commit (`api.moveMessages, copyMessages, deleteMessages`).

---

### Task 5: Frontend — `flagPatch` becomes `listPatch`, plus removal

**Files:**
- Rename: `src/frontend/src/modules/mail/list/flagPatch.ts` → `listPatch.ts` (`git mv`, then fix the imports in `queries.ts` and both test files; `flagPatch.test.ts` → `listPatch.test.ts`)
- Test: `src/frontend/src/modules/mail/list/listPatch.test.ts`

**Interfaces:**
- Produces (Task 6 consumes):

```ts
export interface RemovedSummaries { messages: MailMessageSummary[]; removed: number; removedUnread: number }
export function removeSummaries(messages: MailMessageSummary[], uids: number[]): RemovedSummaries
export interface FolderCountDeltas { total: number; unread: number }
export function patchFolderCounts(tree: MailFolderNode[], folderPath: string, deltas: FolderCountDeltas): MailFolderNode[]
```

`patchFolderUnread` becomes a one-line wrapper over `patchFolderCounts` — one recursion, not two. Same rules: `null` stays `null`, clamp at 0, identity on all-zero deltas.

- [ ] **Step 1: Failing tests.** `removeSummaries`: removes only targets; `removed`/`removedUnread` counted from what was actually present (an absent uid contributes nothing); identity (`toBe`) when nothing matched — the reference-identity gap 2b1's review flagged, closed here. `patchFolderCounts`: both counters on the right node at depth, clamp, null, identity on zero deltas; `patchFolderUnread` still passes its existing suite untouched (rename aside).
- [ ] **Step 2: fail.** **Step 3: Implement** (single pass, `Set` lookup, same style as `patchSummaries`). **Step 4: pass** — run the whole `list/` directory: the rename must break nothing. **Step 5: Commit** (`flagPatch becomes listPatch: row removal and two-counter folder patch`).

---

### Task 6: Frontend — the two mutations, and the poll guard widened

**Files:**
- Modify: `src/frontend/src/modules/mail/queries.ts`
- Modify: `src/frontend/src/modules/mail/list/useListRefresh.ts` (one identifier)
- Test: `src/frontend/src/modules/mail/useMoveMessages.test.tsx` (new), `src/frontend/src/modules/mail/useSetFlags.test.tsx` (key rename only), `src/frontend/src/modules/mail/list/useListRefresh.test.tsx` (one added test)

**Interfaces:**
- Produces: `mailKeys.writes(accountId)` (renamed from `flags` — carried by **all three** write mutations; `useListRefresh`'s `isMutating` filter follows);
  `useMoveMessages(onError?)` taking `{ folderPath, uids, targetFolderPath, copy }`;
  `useDeleteMessages(onError?)` taking `{ folderPath, uids }`.

**Behaviour to implement (shared helper `removeFromFolderCaches(queryClient, accountId, folderPath, uids)` returning `{ snapshots, removed, removedUnread }`, dedup-counting each uid once across pages and stream blocks — the 2b1 `unreadTally` discipline):**

- `useMoveMessages.onMutate`: cancel source pages+stream; `copy: false` → remove rows from source caches, patch source counters `{ total: -removed, unread: -removedUnread }`; `copy: true` → source untouched, counters from `uids.length` (`removedUnread` unknowable without removal — count seen state by scanning, reusing the scan of `removeFromFolderCaches` in dry-run mode is NOT worth it: patch target `total: +uids.length, unread: 0` and let the poll true unread up; say so in a comment). Both modes: snapshot then **remove** target cached pages and stream (`getQueriesData` on both target prefixes → snapshots → `removeQueries`), patch target counters. Rollback restores every snapshot (restoring a removed query via `setQueryData` recreates it — fine).
- `useDeleteMessages.onMutate`: the source half alone.
- Error toasts: `'Could not move the message'` / `'Could not copy the message'` / `'Could not delete the message'`.
- Neither mutation ever touches `invalidateQueries`.

- [ ] **Step 1: Failing tests.** In `useMoveMessages.test.tsx` (2b1's harness — deferred promise, seeded caches, `settle()` before every silence assertion):
  1. move: rows leave source page and stream block; source counters drop by removed/removedUnread; target pages **gone** (`getQueryData` undefined), target counters up.
  2. a uid present in two source blocks: counted once (source `total: -1`).
  3. copy: source page untouched **by identity**; target pages gone; target total up.
  4. rollback: reject → every cache strictly equal to seeded, including the removed target page back in place; `onError` got the message.
  5. never invalidates the stream (spy, `not.toHaveBeenCalled`, after `settle()`).
  6. delete: source-only; no target key touched.
  In `useListRefresh.test.tsx`: `stands down while a move is in flight` — render `useListRefresh` + `useMoveMessages`, pending move, tree patched, `await settle()`, no invalidate. **Break-verify** by giving `useMoveMessages` a different mutationKey and watching this fail.
- [ ] **Step 2: fail** (hooks absent). **Step 3: Implement** — rename `flags` → `writes` first (three call sites: key def, `useSetFlags`, `useListRefresh`), run the two existing suites to prove the rename is pure, then the new hooks. **Step 4: pass; run the whole `modules/mail` directory.** **Step 5: Commit** (`Move/copy/delete mutations: rows out, target caches dropped, one write key`).

---

### Task 7: Frontend — next-message helper

**Files:** `src/frontend/src/modules/mail/list/nextUid.ts` (+ test).

**Interfaces:**
- Produces: `nextUidOf(uids: number[], uid: number): number | null` — the next uid, else the previous at the end, else `null` (absent uid → `null`). Task 11 wires it in `MailLayout`.

- [ ] **Steps 1–5:** four tests (middle → next; last → previous; sole → null; absent → null), fail, implement (indexOf + two branches), pass, commit (`nextUidOf: who follows a departing message`).

---

### Task 8: Frontend — icons

**Files:** `src/frontend/src/icons/ArchiveIcon.tsx`, `FolderMoveIcon.tsx`, `CopyIcon.tsx`, `JunkIcon.tsx`; `icons.test.tsx` (append).

**Interfaces:** all `({ size = 16 })`, Feather style (viewBox 24, stroke 2, `currentColor`). Archive: box + lid + handle line (`<polyline points="21 8 21 21 3 21 3 8"/><rect x="1" y="3" width="22" height="5"/><line x1="10" y1="12" x2="14" y2="12"/>`). FolderMove: folder + arrow. Copy: two offset rectangles (Feather `copy`). Junk: circle + diagonal (Feather `slash`) — the "block" metaphor, distinct from the trash can.

- [ ] **Steps 1–5:** the `icons` table entries + render tests as the file does, fail, implement, pass, commit (`Archive, folder-move, copy and junk icons in the Feather style`).

---

### Task 9: Frontend — `DropdownMenu` separators and disabled items

**Files:** `src/frontend/src/components/DropdownMenu.tsx` (+ test), `src/frontend/src/styles/shell.css` (one rule).

**Interfaces:**
- Produces: `items: MenuEntry[]` where `type MenuEntry = MenuItem | 'separator'`; `MenuItem` gains `disabled?: boolean` and `title?: string` (tooltip for the disabled reason). A disabled item renders a `<button disabled>` that neither closes the menu nor fires. Separator: `<hr className="dropdown-rule" />` (`role="separator"` is implicit on hr), styled `height:1px; border:0; background: var(--border); margin: 4px 6px`.

- [ ] **Step 1: Failing tests:** separator renders between groups (`getAllByRole('separator')`); a disabled item shows its `title`, does not fire `onSelect`, does not close the menu; existing suite (incl. the listener-lifecycle spy) untouched and green.
- [ ] **Steps 2–5:** fail, implement (`items.map` branches on `entry === 'separator'`; key by index for separators), pass, commit (`DropdownMenu: separators and disabled entries`).

---

### Task 10: Frontend — the Move/Copy modal

**Files:**
- Create: `src/frontend/src/modules/mail/folders/folderFilter.ts` (+ test) — pure: `normalizeQuery(s)` (`toLowerCase` + `normalize('NFD')` + strip `\p{M}`), `folderMatches(name, query)`.
- Create: `src/frontend/src/modules/mail/MoveMessagesModal.tsx` (+ test)
- Modify: `src/frontend/src/styles/mail.css` (modal list styles, tokens only)

**Interfaces:**
- Produces:

```tsx
interface Props {
  mode: 'move' | 'copy'
  folders: MailFolderNode[]
  currentFolderPath: string
  onPick: (targetPath: string) => void
  onClose: () => void
}
```

The validated mockup: search input focused on mount (`autoFocus`); list from `flatten(sortFolders(folders))` with `indent(depth)`; filtered by `folderMatches` on the node **name** (children of a filtered-out parent surface flat, which `flatten` + per-row filter gives for free); count line `n of N folders`; rows disabled+badged for `currentFolderPath` (`current`) and non-selectable nodes (`container`); role badges from `specialUse`. Selection state; Enter picks when exactly one enabled row matches; footer `Cancel` + `Move`/`Copy` (primary, disabled without selection); title by mode. House modal skeleton (`modal-overlay`/`modal`/`modal-header`, ✕ closes, `htmlFor`/`id` on the search field). `onPick` closes via the caller.

- [ ] **Step 1: Failing tests.** `folderFilter`: `"indesirable"` matches `"Courrier indésirable"` (**the test that fails without `normalize`**), case-blind, empty query matches all. Modal: search focused on mount; typing filters and updates the count; current folder and containers disabled (click fires nothing); Enter with a single enabled match calls `onPick` with its path; Enter with several does nothing; both modes' title/button; Escape/✕ close.
- [ ] **Steps 2–5:** fail, implement, pass (whole `modules/mail`), commit (`MoveMessagesModal: the filterable picker, accent-blind`).

---

### Task 11: Frontend — list surface: three-button cluster, advance wiring

**Files:**
- Modify: `src/frontend/src/modules/mail/list/MessageList.tsx`
- Modify: `src/frontend/src/modules/mail/MailLayout.tsx`
- Modify: `src/frontend/src/modules/mail/folders/folderNodes.ts` (one helper)
- Modify: `src/frontend/src/styles/mail.css` (reserve 32 → 88px)
- Test: `MessageList.test.tsx`, `folderNodes.test.ts`

**Interfaces:**
- Produces: `rolePathsOf(nodes: MailFolderNode[]): { trash: string | null; archive: string | null; junk: string | null }` in `folderNodes.ts` (via `flatten`, first match per role).
  `MessageList` props gain `onRows?: (uids: number[]) => void` and `onDeparted?: (uid: number) => void`.
  `MailLayout` keeps the latest rows in a **ref** (no re-render), builds `departed(uid)`: if `uid` is the selected one → `nextUidOf` → select or close; passes `onDeparted` to list and reader (reader in Task 12).

**Cluster:** read/unread (2b1) + archive (`ArchiveIcon`) + trash (`TrashIcon` size 16). Archive: `useMoveMessages` to `roles.archive`; disabled (`disabled` + `title="Assign the archive folder in Settings → Folders"`) when the role is unresolved **or** the current folder is the archive. Trash: outside the trash folder → move to `roles.trash` (disabled likewise when unresolved); **in** the trash folder (`folderNode.specialUse === 'trash'`, passed down as a prop `folderRole`) → open `DeleteConfirmModal` (`entityLabel` = the message subject or `(no subject)`), confirm → `useDeleteMessages`. Every action `stopPropagation`, then `onDeparted?.(uid)` (move/delete only — the row leaves optimistically, the selection must follow at once).
`MessageList` reports rows: `useEffect(() => { onRows?.(messages.map(m => m.uid)) }, [messages, onRows])`.

- [ ] **Step 1: Failing tests.** Archive button fires the move with the resolved archive path and does not open the row; trash button moves to trash; in-trash trash button opens the confirm and only confirm fires the delete mutation; unresolved role → button present, `disabled`, carries the `title`; `onDeparted` called with the uid on archive/trash of the selected row; `onRows` reports on render and re-report on change; reserve: the `:nth-last-child(2)` padding value updated in CSS — **and row geometry re-measured in a real browser** (Step 4). `rolePathsOf`: finds each role at depth, null when absent.
- [ ] **Step 2: fail.** **Step 3: Implement** (cluster fragment gains two buttons; `useFolders()` for roles — cached, no extra request; keep the buttons inside the existing `.message-row-cluster`; reserve constant 32 → 88 in `mail.css`, comment updated `3 × 26 + 2 × 2 + 6`).
- [ ] **Step 4: Browser measurement** (the 2b1 probe): narrow skin hovered — preview text ends ≥ 6px clear of the three-button cluster, ellipsis present; wide skin — row height **still 38.5px** at rest and hovered (the recovered height must not regress with two more buttons), cluster still replaces the date. Record numbers in the report.
- [ ] **Step 5: pass, commit** (`Row cluster: archive and trash, selection follows a departing row`).

---

### Task 12: Frontend — reader surface: header Delete, kebab groups, modal wiring

**Files:**
- Modify: `src/frontend/src/modules/mail/reader/ReaderActions.tsx`
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx`
- Modify: `src/frontend/src/modules/mail/MailLayout.tsx` (pass `onDeparted` + `folderRole` to the three readers)
- Modify: `src/frontend/src/styles/mail.css` (`.action-btn.is-danger:hover`)
- Test: `ReaderActions.test.tsx`, `MessageReader.test.tsx`

**Interfaces:**
- `ReaderActions` props gain:

```ts
deleteLabel: 'Delete' | 'Delete permanently'
deleteDisabled: boolean            // no trash role, outside the trash
onDelete: () => void
actions: MenuEntry[]               // the kebab's second group, built by MessageReader
```

Rendered order: `[colour toggle?] [rule?] [delete] [kebab]` — the rule keeps its sole condition (the toggle). Delete button: `className="action-btn is-danger"`, `--text-muted` at rest, `--danger` on hover (`.action-btn.is-danger:hover { color: var(--danger); background: color-mix(in oklab, currentColor 14%, transparent) }` — the row-button tint technique). Kebab items = the two flag entries (2b1), `'separator'`, then `actions`.

- `MessageReader` builds it all: `useFolders` → `rolePathsOf` + current `folderRole` prop; `useMoveMessages`/`useDeleteMessages` with `onNotify`; modal state `{ mode } | null`; confirm state for permanent delete. Handlers: delete (trash-move or confirm→expunge, then `onDeparted?.(uid)`); archive, junk (role moves, disabled entries when unresolved or already there — `disabled` + `title`); `Move to…` / `Copy to…` open `MoveMessagesModal` (`folders` from cache, `currentFolderPath`); `onPick` fires the mutation (+`onDeparted` for move only, a copy departs nothing) and closes.

- [ ] **Step 1: Failing tests.** ReaderActions: delete button present both themes, label/aria per `deleteLabel`, fires `onDelete`, rule still bound to the toggle alone; kebab renders the separator and the four action entries; disabled entry carries its title and fires nothing. MessageReader: delete outside trash fires the move to the trash path and `onDeparted`; in trash (`folderRole="trash"`) opens the confirm, confirm fires `deleteMessages` with `[uid]`; archive entry moves to the archive path; junk disabled without a junk role (present, `disabled`); `Move to…` opens the modal, picking a folder fires the move with that target and departs; `Copy to…` picks without departing. Mock the mutations at the `api` level as 2b1's reader tests do.
- [ ] **Step 2: fail.** **Step 3: Implement.** **Step 4: pass — full frontend suite** (the reader tests' api mock gains the three new methods). **Step 5: Commit** (`Reader: header Delete, grouped kebab, move/copy through the picker`).

---

### Task 13: Full verification + docs

- [ ] **Step 1:** backend `dotnet test`; frontend `npx vitest run && npm run build && npx eslint src && npx tsc --noEmit` — all green, tree clean.
- [ ] **Step 2: Update the CLAUDE.md claims now, not post-review** (2b1's final review caught them stale): `src/frontend/CLAUDE.md` — the "Project" line (flags **and** message actions are in; composing and search are not) and the reader-header paragraph (Delete in the actions zone, the kebab's two groups, the picker modal); `src/snoopy.microservice/CLAUDE.md` — append the three endpoints to the MailController line with the UIDPLUS refusal semantics.
- [ ] **Step 3:** commit leftovers (`2b2 verification and doc truth`).

---

## Self-review notes

- Spec §3 (endpoints, validation order, session-side selectability, UIDPLUS refusal) → Tasks 1–3. §4 (mutations, asymmetric patch, dropped target caches, widened guard, advance) → Tasks 5–7, 11. §5 (cluster, header Delete, kebab groups, modal, activation rules, icons) → Tasks 8–12. §6 tests distributed per task; §7 exclusions honoured (no undo, no success toasts, no rspamd).
- Known asymmetry stated in-plan: copy patches target `unread` by 0 and lets the poll true it — deliberate, commented.
- Type names cross-checked: `TargetNotSelectable`, `MoveOrCopyAsync`, `RemovedSummaries`, `patchFolderCounts`, `mailKeys.writes`, `MenuEntry`, `rolePathsOf`, `nextUidOf`, `onDeparted`/`onRows`, `folderRole` — each defined once, consumed by name.
