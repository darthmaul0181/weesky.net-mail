# Periodic mail refresh — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** mail that arrives, is deleted, read or flagged elsewhere shows in the open webmail
within a minute, without the user doing anything.

**Architecture:** `GET /Mail/Folders` is already a single cheap `LIST`+`STATUS` across all
folders; it gains `UIDNEXT` and (capability-gated) `HIGHESTMODSEQ`. The client polls it through
TanStack's `refetchInterval` — the tree badges update for free — and a watcher hook compares
snapshots of the displayed folder to decide when to refresh the list.

**Tech Stack:** .NET 10 + MailKit + xUnit; React 18 + TypeScript, TanStack Query v5, Vitest.

**Spec:** `docs/superpowers/specs/2026-07-21-webmail-periodic-refresh-design.md`

## Global Constraints

- `POLL_INTERVAL = 60_000` — internal constant; never stored, shown, or configurable.
- `HIGHESTMODSEQ` is requested ONLY when the server advertises `CONDSTORE`; asking for a
  `STATUS` item never advertised is a protocol error. Same pattern as `MailboxId`/`OBJECTID`.
- `folderChanged` trips on `uidNext`, `total`, `unread` OR `highestModSeq` moving — both values
  non-null. Null-to-value is discovery, not change, and must never trip.
- A `uidValidity` change trips `uidValidityBroke` and NOT the ordinary refresh.
- **Streaming refresh must never go through `invalidateQueries` on the stream key** — TanStack
  v5 would refetch every loaded block: forty blocks = forty IMAP connections and forty full
  folder sorts. Block 0 is fetched alone and swapped into the cache.
- A failed poll or refresh is silent: no toast, no error state, list keeps its content.
- Baseline rule: the first observation of a folder is the baseline; it never triggers a refresh.
- Project rules (CLAUDE.md): comments only where the code is not self-evident, 3 lines max; no
  code duplication; think about performance; UI copy in English; commit messages two lines max.
- `dotnet test` (not `--no-build`) whenever a new test file is added.

---

### Task 1: Backend — the poll answers change counters

**Files:**
- Modify: `src/snoopy.microservice/Services/ImapSession.cs:40-42` (status items), `:70-75` (mapping)
- Modify: `src/snoopy.microservice/Models/Mail/MailFolderNode.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapSessionListFoldersTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `MailFolderNode.UidNext` (`uint?`) and `MailFolderNode.HighestModSeq` (`ulong?`),
  serialised to the client as `uidNext` / `highestModSeq`, null when not selectable or (for
  `HighestModSeq`) when the server lacks `CONDSTORE`.

- [ ] **Step 1: Extend the fake IMAP server**

`ImapSessionListFoldersTests.cs` contains `FakeImapServer`, a loopback listener scripting just
enough IMAP. Give it a constructor flag and richer `STATUS` items:

```csharp
internal sealed class FakeImapServer : IDisposable
{
    private readonly bool _condStore;

    public FakeImapServer(bool condStore = false) => _condStore = condStore;
```

Derive the capability string once and use it in the three places capabilities are written
(greeting, LOGIN response, CAPABILITY response):

```csharp
    private string Caps => _condStore
        ? "IMAP4rev1 NAMESPACE SPECIAL-USE CONDSTORE"
        : "IMAP4rev1 NAMESPACE SPECIAL-USE";
```

(greeting: `$"* OK [CAPABILITY {Caps}] Fake IMAP ready"`, and likewise for the other two.)

Change the `STATUS` case so the answer carries the new items only when the fake advertises them:

```csharp
                    case "STATUS":
                        var mailbox = ExtractMailboxName(parts.Length > 2 ? parts[2] : string.Empty);
                        var items = _condStore
                            ? "MESSAGES 3 UNSEEN 1 UIDVALIDITY 100 UIDNEXT 4 HIGHESTMODSEQ 42"
                            : "MESSAGES 3 UNSEEN 1 UIDVALIDITY 100 UIDNEXT 4";
                        await writer.WriteLineAsync($"* STATUS {mailbox} ({items})");
                        await writer.WriteLineAsync($"{tag} OK STATUS completed");
                        break;
```

- [ ] **Step 2: Write the failing tests**

Add to `ImapSessionListFoldersTests` (same arrangement as the existing test — real client,
fake server):

```csharp
    // The poll's change signal. UIDNEXT rises on every arrival; HIGHESTMODSEQ on every flag
    // change, which no counter sees — but only a CONDSTORE server may be asked for it.
    [Fact]
    public async Task ListFoldersAsync_CarriesTheChangeCountersWhenCondStoreIsAdvertised()
    {
        using var server = new FakeImapServer(condStore: true);
        server.Start();

        using var client = new ImapClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync("127.0.0.1", server.Port, SecureSocketOptions.None, cts.Token);
        await client.AuthenticateAsync("alice", "hunter2", cts.Token);
        await using var session = new ImapSession(client, Mock.Of<IMailHtmlSanitizer>(), Mock.Of<ILogger>());

        var result = await session.ListFoldersAsync(cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var inbox = Assert.Single(result.Value, n => n.Path == "INBOX");
        Assert.Equal(4u, inbox.UidNext);
        Assert.Equal(42ul, inbox.HighestModSeq);
    }

    [Fact]
    public async Task ListFoldersAsync_LeavesHighestModSeqNullWithoutCondStore()
    {
        using var server = new FakeImapServer();
        server.Start();

        using var client = new ImapClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync("127.0.0.1", server.Port, SecureSocketOptions.None, cts.Token);
        await client.AuthenticateAsync("alice", "hunter2", cts.Token);
        await using var session = new ImapSession(client, Mock.Of<IMailHtmlSanitizer>(), Mock.Of<ILogger>());

        var result = await session.ListFoldersAsync(cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var inbox = Assert.Single(result.Value, n => n.Path == "INBOX");
        Assert.Equal(4u, inbox.UidNext);
        Assert.Null(inbox.HighestModSeq);
    }
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests --filter ImapSessionListFoldersTests`

Expected: FAIL — `MailFolderNode` has no `UidNext` member (compile error is the failure here).

- [ ] **Step 4: Add the fields and the mapping**

In `MailFolderNode.cs`, after `UidValidity`:

```csharp
    /// <summary>Next UID the folder will assign. Rises on every arrival — the poll's signal
    /// for new mail. Null when not selectable.</summary>
    public uint? UidNext { get; set; }

    /// <summary>RFC 7162 modification counter; rises on every flag change, which no message
    /// count sees. Null when the server lacks CONDSTORE or the folder is not selectable.</summary>
    public ulong? HighestModSeq { get; set; }
```

In `ImapSession.ListFoldersAsync`, extend the status items (the comment above them already
explains the gating rule):

```csharp
            var statusItems = StatusItems.Count | StatusItems.Unread | StatusItems.UidValidity
                              | StatusItems.UidNext;
            if (_client.Capabilities.HasFlag(ImapCapabilities.ObjectID))
                statusItems |= StatusItems.MailboxId;
            var condStore = _client.Capabilities.HasFlag(ImapCapabilities.CondStore);
            if (condStore)
                statusItems |= StatusItems.HighestModSeq;
```

And in the node construction, after `UidValidity = folder.UidValidity`:

```csharp
                    UidNext = selectable ? folder.UidNext?.Id : null,
                    HighestModSeq = selectable && condStore ? folder.HighestModSeq : null
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests --filter ImapSessionListFoldersTests`

Expected: PASS — the two new tests and the pre-existing one (whose fake now answers `UIDNEXT`
too; it asserts nothing about it).

- [ ] **Step 6: Run the whole backend suite**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests`

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/snoopy.microservice/Services/ImapSession.cs \
        src/snoopy.microservice/Models/Mail/MailFolderNode.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapSessionListFoldersTests.cs
git commit -m "Report UIDNEXT and HIGHESTMODSEQ on the folder listing

HIGHESTMODSEQ gated on CONDSTORE; flag changes move no other counter."
```

---

### Task 2: The change detector

**Files:**
- Modify: `src/frontend/src/modules/mail/api/mailTypes.ts` (MailFolderNode)
- Create: `src/frontend/src/modules/mail/list/folderDelta.ts`
- Test: `src/frontend/src/modules/mail/list/folderDelta.test.ts`

**Interfaces:**
- Consumes: nothing from earlier tasks (types only).
- Produces:
  - `MailFolderNode.uidNext: number | null`, `MailFolderNode.highestModSeq: number | null`
  - `FolderSnapshot`, `snapshotOf(node: MailFolderNode): FolderSnapshot`
  - `folderChanged(previous: FolderSnapshot, next: FolderSnapshot): boolean`
  - `uidValidityBroke(previous: FolderSnapshot, next: FolderSnapshot): boolean`

- [ ] **Step 1: Extend the frontend type**

In `mailTypes.ts`, inside `MailFolderNode` after `uidValidity: number`:

```ts
  /** Rises on every arrival — the poll's signal for new mail. Null when not selectable. */
  uidNext: number | null
  /** Rises on every flag change (RFC 7162). Null without CONDSTORE or when not selectable. */
  highestModSeq: number | null
```

- [ ] **Step 2: Write the failing tests**

Create `src/frontend/src/modules/mail/list/folderDelta.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import type { FolderSnapshot } from './folderDelta'
import { folderChanged, uidValidityBroke } from './folderDelta'

const base: FolderSnapshot = { uidNext: 10, total: 5, unread: 2, highestModSeq: 40, uidValidity: 100 }

describe('folderChanged', () => {
  it('sees nothing when nothing moved', () => {
    expect(folderChanged(base, { ...base })).toBe(false)
  })

  it.each([
    ['an arrival', { ...base, uidNext: 11 }],
    ['a deletion made elsewhere', { ...base, total: 4 }],
    ['a read/unread flip elsewhere', { ...base, unread: 1 }],
    ['a flags-only change elsewhere', { ...base, highestModSeq: 41 }],
  ])('sees %s', (_label, next) => {
    expect(folderChanged(base, next)).toBe(true)
  })

  // A server without CONDSTORE answers null forever; a null must never look like a change.
  it('ignores fields the server does not report', () => {
    const previous = { ...base, highestModSeq: null }
    expect(folderChanged(previous, { ...previous })).toBe(false)
  })

  // Null-to-value is discovery, not change: without this, the first poll after a deploy
  // that adds a counter would refresh every open list for nothing.
  it('does not fire on discovery of a counter', () => {
    expect(folderChanged({ ...base, highestModSeq: null }, base)).toBe(false)
    expect(folderChanged({ ...base, uidNext: null }, base)).toBe(false)
  })

  it('leaves uidValidity to uidValidityBroke', () => {
    expect(folderChanged(base, { ...base, uidValidity: 101 })).toBe(false)
  })
})

describe('uidValidityBroke', () => {
  it('fires only when uidValidity moved', () => {
    expect(uidValidityBroke(base, { ...base, uidValidity: 101 })).toBe(true)
    expect(uidValidityBroke(base, { ...base, uidNext: 11 })).toBe(false)
  })
})
```

- [ ] **Step 3: Run to verify it fails**

Run: `cd src/frontend && npm run test -- folderDelta`

Expected: FAIL — `Failed to resolve import "./folderDelta"`.

- [ ] **Step 4: Write the implementation**

Create `src/frontend/src/modules/mail/list/folderDelta.ts`:

```ts
import type { MailFolderNode } from '../api/mailTypes'

/** What the poll compares between two ticks. All change signals, nothing else. */
export interface FolderSnapshot {
  uidNext: number | null
  total: number | null
  unread: number | null
  highestModSeq: number | null
  uidValidity: number
}

export function snapshotOf(node: MailFolderNode): FolderSnapshot {
  return {
    uidNext: node.uidNext,
    total: node.total,
    unread: node.unread,
    highestModSeq: node.highestModSeq,
    uidValidity: node.uidValidity,
  }
}

// Null-to-value is discovery (a counter the server just started reporting), not change.
function moved(previous: number | null, next: number | null): boolean {
  return previous !== null && next !== null && previous !== next
}

export function folderChanged(previous: FolderSnapshot, next: FolderSnapshot): boolean {
  return moved(previous.uidNext, next.uidNext)
    || moved(previous.total, next.total)
    || moved(previous.unread, next.unread)
    || moved(previous.highestModSeq, next.highestModSeq)
}

/** Every cached UID for the folder is a lie when this fires. */
export function uidValidityBroke(previous: FolderSnapshot, next: FolderSnapshot): boolean {
  return previous.uidValidity !== next.uidValidity
}
```

- [ ] **Step 5: Run to verify it passes, then typecheck**

Run: `cd src/frontend && npm run test -- folderDelta && npm run typecheck`

Expected: tests PASS. Typecheck may fail where test fixtures build `MailFolderNode` objects
without the two new fields — fix ONLY by adding `uidNext: null, highestModSeq: null` to those
fixtures, nothing else.

- [ ] **Step 6: Run the whole suite and commit**

Run: `cd src/frontend && npm run test && npm run lint`

```bash
git add src/frontend/src/modules/mail/api/mailTypes.ts \
        src/frontend/src/modules/mail/list/folderDelta.ts \
        src/frontend/src/modules/mail/list/folderDelta.test.ts
git add -u src/frontend/src   # fixture fixes, if any
git commit -m "Add the folder change detector

Four counters move it; null is discovery; uidValidity is its own verdict."
```

---

### Task 3: The poll

**Files:**
- Modify: `src/frontend/src/modules/mail/queries.ts:23-34`
- Test: `src/frontend/src/modules/mail/queries.test.tsx`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `export const POLL_INTERVAL = 60_000`
  - `useFolders()` now polls at that interval.
  - `useAccountId()` becomes exported (Task 4 needs it for cache keys).

- [ ] **Step 1: Write the failing test**

Append to the `useFolders` describe block in `queries.test.tsx`:

```tsx
  // The poll: one LIST+STATUS a minute. TanStack pauses it while the tab is unfocused and
  // the app-wide refetchOnWindowFocus fires the catch-up tick on return.
  it('polls the folders every minute', async () => {
    vi.useFakeTimers()
    try {
      mocks.getMailFolders.mockResolvedValue([])
      const { wrapper } = createWrapper()
      renderHook(() => useFolders(), { wrapper })

      await act(async () => { await vi.advanceTimersByTimeAsync(0) })
      expect(mocks.getMailFolders).toHaveBeenCalledTimes(1)

      await act(async () => { await vi.advanceTimersByTimeAsync(POLL_INTERVAL) })
      expect(mocks.getMailFolders).toHaveBeenCalledTimes(2)
    } finally {
      vi.useRealTimers()
    }
  })
```

Import `act` from `@testing-library/react` and `POLL_INTERVAL` from `./queries` alongside the
existing imports.

- [ ] **Step 2: Run to verify it fails**

Run: `cd src/frontend && npm run test -- queries`

Expected: FAIL — `No "POLL_INTERVAL" export is defined on the module` (and, once exported
without the interval, the second assertion fails at 1 call).

- [ ] **Step 3: Implement**

In `queries.ts`, export the constant above `useFolders` and set the interval:

```ts
/** One cheap LIST+STATUS across all folders. Internal, like BLOCK_SIZE: not a setting. */
export const POLL_INTERVAL = 60_000

export function useFolders() {
  const accountId = useAccountId()

  return useQuery<MailFolderNode[]>({
    queryKey: mailKeys.folders(accountId),
    queryFn: ({ signal }) => api.getMailFolders({ signal }),
    refetchInterval: POLL_INTERVAL,
  })
}
```

Change `function useAccountId()` to `export function useAccountId()`.

- [ ] **Step 4: Run to verify it passes**

Run: `cd src/frontend && npm run test -- queries`

Expected: PASS. If the fake-timer test proves flaky against TanStack's scheduler, replace it
with an options assertion — find the query through `client.getQueryCache()` and assert
`options.refetchInterval === POLL_INTERVAL` — and say so in the report; do not ship a flaky
timer test.

- [ ] **Step 5: Typecheck, lint, full suite, commit**

Run: `cd src/frontend && npm run typecheck && npm run lint && npm run test`

```bash
git add src/frontend/src/modules/mail/queries.ts src/frontend/src/modules/mail/queries.test.tsx
git commit -m "Poll the folder listing every minute

The tree badges ride the same query; the list watcher comes next."
```

---

### Task 4: The watcher

**Files:**
- Create: `src/frontend/src/modules/mail/list/useListRefresh.ts`
- Modify: `src/frontend/src/modules/mail/MailLayout.tsx` (one hook call)
- Test: `src/frontend/src/modules/mail/list/useListRefresh.test.tsx`
- Test: `src/frontend/src/modules/mail/MailLayout.test.tsx` (one wiring test)

**Interfaces:**
- Consumes: `folderChanged`, `uidValidityBroke`, `snapshotOf`, `FolderSnapshot` (Task 2);
  `useFolders`, `useAccountId`, `mailKeys` (Task 3); `BLOCK_SIZE`, `isStreaming`,
  `usePreferences` (existing); `flatten` from `../folders/folderNodes` (returns
  `Array<{ node, depth }>`); `api.getMailMessages(folder, page, pageSize)`.
- Produces: `useListRefresh(folderPath: string | null): void` — side effects only.

- [ ] **Step 1: Write the failing tests**

Create `src/frontend/src/modules/mail/list/useListRefresh.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider, type InfiniteData } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import type { MailFolderNode, MailFolderPage } from '../api/mailTypes'
import { mailKeys } from '../queries'
import { useListRefresh } from './useListRefresh'

const mocks = vi.hoisted(() => ({
  getMailFolders: vi.fn(), getMailMessages: vi.fn(), getPreferences: vi.fn(),
}))
vi.mock('../../../api.js', () => ({ api: mocks }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' } }),
}))

let client: QueryClient
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

function inbox(overrides: Partial<MailFolderNode> = {}): MailFolderNode {
  return {
    path: 'INBOX', name: 'INBOX', specialUse: null, selectable: true, subscribed: true,
    total: 5, unread: 2, uidValidity: 100, uidNext: 10, highestModSeq: 40, children: [],
    ...overrides,
  }
}

function pageOf(uids: number[]): MailFolderPage {
  return {
    folderPath: 'INBOX', uidValidity: 100, total: 5, page: 0, pageSize: uids.length,
    messages: uids.map(uid => ({
      uid, subject: '', fromName: '', fromAddress: '', date: '2026-07-21T00:00:00Z',
      seen: true, flagged: false, answered: false, hasAttachments: false, size: 0, preview: '',
    })),
  }
}

/** Renders the hook, waits for the baseline snapshot, then applies the next poll answer. */
async function renderWithBaseline(pageSize: string, first: MailFolderNode) {
  mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': pageSize, 'mail.showPreview': 'true' })
  mocks.getMailFolders.mockResolvedValue([first])

  const rendered = renderHook(() => useListRefresh('INBOX'), { wrapper })
  await waitFor(() => expect(mocks.getMailFolders).toHaveBeenCalled())
  await waitFor(() =>
    expect(client.getQueryData(mailKeys.folders('primary'))).toBeDefined())

  return {
    ...rendered,
    tick: (next: MailFolderNode) =>
      act(() => { client.setQueryData(mailKeys.folders('primary'), [next]) }),
  }
}

describe('useListRefresh', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  })

  it('does nothing on the baseline observation', async () => {
    const spy = vi.spyOn(client, 'invalidateQueries')
    await renderWithBaseline('10', inbox())

    expect(spy).not.toHaveBeenCalled()
    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  it('refreshes the paged list when the folder moved', async () => {
    const spy = vi.spyOn(client, 'invalidateQueries')
    const { tick } = await renderWithBaseline('10', inbox())

    await tick(inbox({ uidNext: 11, total: 6, unread: 3 }))

    await waitFor(() => expect(spy).toHaveBeenCalledWith(
      { queryKey: ['mail', 'primary', 'messages', 'INBOX'] }))
  })

  it('stays quiet when nothing moved', async () => {
    const spy = vi.spyOn(client, 'invalidateQueries')
    const { tick } = await renderWithBaseline('10', inbox())

    await tick(inbox())

    expect(spy).not.toHaveBeenCalled()
  })

  // THE lock of this feature: three loaded blocks, a delta, and exactly ONE request goes
  // out — block 0. invalidateQueries on the stream would refetch every block.
  it('refreshes one block in streaming mode, never the whole stream', async () => {
    const spy = vi.spyOn(client, 'invalidateQueries')
    const { tick } = await renderWithBaseline('all', inbox())

    const key = mailKeys.messageStream('primary', 'INBOX', 100)
    client.setQueryData<InfiniteData<MailFolderPage>>(key, {
      pages: [pageOf([30, 29]), pageOf([28, 27]), pageOf([26, 25])],
      pageParams: [0, 1, 2],
    })
    mocks.getMailMessages.mockResolvedValue(pageOf([31, 30]))

    await tick(inbox({ uidNext: 12 }))

    await waitFor(() => expect(mocks.getMailMessages).toHaveBeenCalledTimes(1))
    expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 0, 100)
    expect(spy).not.toHaveBeenCalled()

    const data = client.getQueryData<InfiniteData<MailFolderPage>>(key)!
    expect(data.pages).toHaveLength(3)
    expect(data.pages[0].messages.map(m => m.uid)).toEqual([31, 30])
    expect(data.pages[1].messages.map(m => m.uid)).toEqual([28, 27])
  })

  it('resets the folder outright when uidValidity broke', async () => {
    const reset = vi.spyOn(client, 'resetQueries')
    const invalidate = vi.spyOn(client, 'invalidateQueries')
    const { tick } = await renderWithBaseline('10', inbox())

    await tick(inbox({ uidValidity: 101 }))

    await waitFor(() => expect(reset).toHaveBeenCalledTimes(1))
    expect(invalidate).not.toHaveBeenCalled()
    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  it('swallows a failed block-0 refresh in silence', async () => {
    const { tick } = await renderWithBaseline('all', inbox())
    mocks.getMailMessages.mockRejectedValue(new Error('refused'))

    await tick(inbox({ uidNext: 12 }))
    await waitFor(() => expect(mocks.getMailMessages).toHaveBeenCalled())
    // Nothing to assert beyond "no crash": the list keeps whatever it had.
  })

  it('re-baselines when the displayed folder changes', async () => {
    const spy = vi.spyOn(client, 'invalidateQueries')
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '10', 'mail.showPreview': 'true' })
    mocks.getMailFolders.mockResolvedValue([inbox(), inbox({ path: 'Archive', name: 'Archive', uidNext: 50 })])

    const { rerender } = renderHook(({ path }) => useListRefresh(path), {
      wrapper, initialProps: { path: 'INBOX' as string | null },
    })
    await waitFor(() =>
      expect(client.getQueryData(mailKeys.folders('primary'))).toBeDefined())

    rerender({ path: 'Archive' })
    // Archive's first observation is a baseline, not a change against INBOX's snapshot.
    expect(spy).not.toHaveBeenCalled()
  })
})
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd src/frontend && npm run test -- useListRefresh`

Expected: FAIL — `Failed to resolve import "./useListRefresh"`.

- [ ] **Step 3: Write the hook**

Create `src/frontend/src/modules/mail/list/useListRefresh.ts`:

```ts
import { useEffect, useRef } from 'react'
import { useQueryClient, type InfiniteData, type QueryClient } from '@tanstack/react-query'
import { api } from '../../../api.js'
import { BLOCK_SIZE, isStreaming, usePreferences } from '../../../hooks/usePreferences'
import type { MailFolderPage } from '../api/mailTypes'
import { flatten } from '../folders/folderNodes'
import { mailKeys, useAccountId, useFolders } from '../queries'
import { folderChanged, snapshotOf, uidValidityBroke, type FolderSnapshot } from './folderDelta'

/** Fetches block 0 alone and swaps it in. Never invalidates: that would refetch EVERY loaded
    block — forty blocks would be forty IMAP connections and forty full folder sorts. */
async function refreshFirstBlock(client: QueryClient, accountId: string, folder: string) {
  const key = mailKeys.messageStream(accountId, folder, BLOCK_SIZE)
  try {
    const fresh: MailFolderPage = await api.getMailMessages(folder, 0, BLOCK_SIZE)
    client.setQueryData<InfiniteData<MailFolderPage>>(key, old =>
      old ? { ...old, pages: [fresh, ...old.pages.slice(1)] } : old)
  } catch {
    // A poll-driven refresh fails in silence; the next tick tries again.
  }
}

/**
 * Watches the polled folder listing and refreshes the displayed list when its folder moved.
 * The first observation of a folder is the baseline and never triggers.
 */
export function useListRefresh(folderPath: string | null): void {
  const accountId = useAccountId()
  const client = useQueryClient()
  const { data: folders } = useFolders()
  const { data: preferences } = usePreferences()
  const previous = useRef<{ path: string; snapshot: FolderSnapshot } | null>(null)

  useEffect(() => {
    if (!folderPath || !folders || !preferences) return
    const node = flatten(folders).find(entry => entry.node.path === folderPath)?.node
    if (!node) return

    const snapshot = snapshotOf(node)
    const last = previous.current
    previous.current = { path: folderPath, snapshot }
    if (!last || last.path !== folderPath) return

    if (uidValidityBroke(last.snapshot, snapshot)) {
      // Every cached UID is a lie. resetQueries refetches only what is on screen, from
      // scratch — an invalidate would replay every loaded stream block.
      client.resetQueries({
        predicate: query =>
          query.queryKey[0] === 'mail' && query.queryKey[1] === accountId
          && query.queryKey[3] === folderPath,
      })
      return
    }

    if (!folderChanged(last.snapshot, snapshot)) return

    if (isStreaming(preferences)) {
      refreshFirstBlock(client, accountId, folderPath)
    } else {
      client.invalidateQueries({ queryKey: ['mail', accountId, 'messages', folderPath] })
    }
  }, [folders, folderPath, preferences, accountId, client])
}
```

Note on the reset predicate: `queryKey[3]` is the folder path in all three folder-scoped key
shapes (`messages`, `messageStream`, `message`), which is what makes one predicate cover them.

- [ ] **Step 4: Run to verify it passes**

Run: `cd src/frontend && npm run test -- useListRefresh`

Expected: PASS, 7 tests.

- [ ] **Step 5: Wire it into the layout**

In `MailLayout.tsx`, import and call it where the selected folder is known (beside the existing
hooks; `folder` is the selected path variable already in scope):

```tsx
import { useListRefresh } from './list/useListRefresh'
```

```tsx
  useListRefresh(folder)
```

Add one wiring test to `MailLayout.test.tsx`, following that file's existing mock arrangement:

```tsx
vi.mock('./list/useListRefresh', () => ({ useListRefresh: mocks.useListRefresh }))
```

(with `useListRefresh: vi.fn()` added to its hoisted mocks), and:

```tsx
  it('watches the displayed folder for remote changes', async () => {
    renderLayout()

    await waitFor(() => expect(mocks.useListRefresh).toHaveBeenCalledWith('INBOX'))
  })
```

Adapt `renderLayout()` and the expected folder to the file's existing helpers and fixtures.

- [ ] **Step 6: Typecheck, lint, full suite**

Run: `cd src/frontend && npm run typecheck && npm run lint && npm run test`

Expected: all clean. Lint has 3 pre-existing warnings; 0 errors is the bar.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/modules/mail/list/useListRefresh.ts \
        src/frontend/src/modules/mail/list/useListRefresh.test.tsx \
        src/frontend/src/modules/mail/MailLayout.tsx \
        src/frontend/src/modules/mail/MailLayout.test.tsx
git commit -m "Refresh the list when the poll sees the folder move

Streaming swaps block 0 in place; uidValidity resets the folder outright."
```

---

### Task 5: Document the slice

**Files:**
- Modify: `src/frontend/CLAUDE.md`

**Interfaces:**
- Consumes: the finished feature.
- Produces: nothing code-facing.

- [ ] **Step 1: Update the frontend guide**

Extend the mail-module notes in the guide's established register — the *why* and the
counter-example, not an inventory. Cover:

- The poll is `refetchInterval: POLL_INTERVAL` on `useFolders`, not a timer: the tree badges
  ride the same answer, all folders at once. It pauses unfocused; the app-wide focus refetch is
  the catch-up tick.
- `folderChanged` trips on four counters because each covers a blindness of the others: a
  deletion elsewhere moves no `uidNext`; a flag change moves no counter at all, which is what
  `HIGHESTMODSEQ` (CONDSTORE-gated, null tolerated) exists for. Null-to-value is discovery,
  never change.
- Scroll is deliberately not compensated: Rainloop and Outlook Web were tested by hand and
  neither does — rows shift down when mail lands, the reading pane never moves. Do not
  reintroduce compensation as a "fix".
- A streaming refresh swaps block 0 by `setQueryData` and never invalidates the stream key; a
  `uidValidity` break goes through `resetQueries` for the same reason — an invalidate replays
  every loaded block.
- Poll and refresh failures are silent by design: at a tick a minute, a toast per blip would be
  unbearable.

- [ ] **Step 2: Commit**

```bash
git add src/frontend/CLAUDE.md
git commit -m "Document the periodic refresh

Records the four-counter rationale and the no-compensation decision."
```
