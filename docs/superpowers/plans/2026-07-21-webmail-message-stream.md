# "All" messages per page — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** add `All` to the messages-per-page setting; the list then drops its pager and loads
blocks of 100 as the reader scrolls.

**Architecture:** the backend gains one allowed value and nothing else — in streaming mode the
client asks for ordinary pages of 100, one after another. On the client, a single hook
(`useMessageList`) answers one shape in both modes, so `MessageList` renders a pager or a sentinel
and never learns what "All" means.

**Tech Stack:** React 18 + TypeScript, react-router-dom v6, TanStack Query v5 (`useInfiniteQuery`),
Vitest + jsdom + Testing Library. Backend: .NET 10, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-21-webmail-message-stream-design.md`

## Global Constraints

- `BLOCK_SIZE = 100` — internal constant; never stored, shown, or configurable.
- `PREFETCH_ROWS = 20` — internal constant; the sentinel sits 20 rows before the last, measured
  in rows and never in pixels (row height changes with the preview setting).
- The preference value for streaming is the exact string `"all"`; the combo label is `All`.
- `isStreaming` is the **only** place in the codebase comparing against `"all"`. No accessor may
  ever return `NaN`.
- The stream query sets `refetchOnWindowFocus: false`. Omitting it means 40 IMAP connections on a
  return to the tab.
- The stream stops on `lastPage.messages.length < requestSize`, never on `total`.
- A failed block never clears the loaded rows; it offers Retry.
- Counter format: `Intl.NumberFormat('en-US')` — an explicit locale, never the machine's.
- UI copy is English. Comments follow the repo rules: none where the code is self-evident, three
  lines maximum where one is warranted.
- `dotnet test` (not `--no-build`) whenever a new test file is added.

---

### Task 1: Backend accepts `all`

**Files:**
- Modify: `src/snoopy.microservice/Models/UserPreferences.cs:24`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Models/UserPreferencesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `UserPreferences.MailPageSize` now accepts `"all"`; `PUT /api/Preferences` answers 204
  for it.

- [ ] **Step 1: Add the failing cases to the existing theory**

In `UserPreferencesTests.cs`, add two rows to the `IsValid_AcceptsOnlyTheOfferedValues` theory,
directly after the `"100", true` row:

```csharp
    [InlineData(UserPreferences.MailPageSize, "all", true)]
    [InlineData(UserPreferences.MailPageSize, "ALL", false)]   // the value is a symbol, not prose
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests --filter IsValid_AcceptsOnlyTheOfferedValues`

Expected: FAIL — the `"all"` case reports `Assert.Equal() Failure: Expected: True, Actual: False`.

- [ ] **Step 3: Add the value to the registry**

In `UserPreferences.cs`, change the `MailPageSize` definition:

```csharp
        new(MailPageSize, "30", ["10", "20", "30", "50", "100", "all"]),
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests --filter UserPreferencesTests`

Expected: PASS, all cases.

- [ ] **Step 5: Add the controller test**

In `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/PreferencesControllerTests.cs`,
read the existing test that PUTs a valid value and copy its arrangement exactly, changing only the
value. Name it:

```csharp
    [Fact]
    public async Task Put_AcceptsAll()
```

It must assert `Assert.IsType<NoContentResult>(result)` — the exact type, not a base class.

- [ ] **Step 6: Run the whole backend suite**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests`

Expected: PASS, no failures.

- [ ] **Step 7: Commit**

```bash
git add src/snoopy.microservice/Models/UserPreferences.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Models/UserPreferencesTests.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Controllers/PreferencesControllerTests.cs
git commit -m "Accept \"all\" as a messages-per-page value

The list reads it; the wire never sees it — it asks for ordinary pages."
```

---

### Task 2: Preference accessors and the settings combo

**Files:**
- Modify: `src/frontend/src/hooks/usePreferences.ts:43-49`
- Modify: `src/frontend/src/modules/settings/general/GeneralPage.tsx`
- Test: `src/frontend/src/hooks/usePreferences.test.tsx`
- Test: `src/frontend/src/modules/settings/general/GeneralPage.test.tsx`

**Interfaces:**
- Consumes: `PREFERENCE_KEYS.pageSize` (existing).
- Produces:
  - `export const BLOCK_SIZE = 100`
  - `export function requestSizeOf(preferences: Preferences): number`
  - `export function isStreaming(preferences: Preferences): boolean`
  - `pageSizeOf` is **removed**; its only non-test callers are `MessageList.tsx:29` and
    `GeneralPage.tsx:40`, both updated in this task.

- [ ] **Step 1: Write the failing accessor tests**

In `src/frontend/src/hooks/usePreferences.test.tsx`, replace the `describe('the accessors')` block's
page-size test with:

```tsx
describe('the accessors', () => {
  it('reads a numeric page size as a number', () => {
    expect(requestSizeOf({ [PREFERENCE_KEYS.pageSize]: '100' })).toBe(100)
  })

  // "all" is a string in a field every other value fills with a number. Number('all') is NaN,
  // and NaN throws nothing: it would travel into the request URL and surface as an empty list.
  it('reads "all" as the block size, never NaN', () => {
    const preferences = { [PREFERENCE_KEYS.pageSize]: 'all' }

    expect(requestSizeOf(preferences)).toBe(BLOCK_SIZE)
    expect(Number.isNaN(requestSizeOf(preferences))).toBe(false)
  })

  it.each([
    ['all', true],
    ['30', false],
    ['100', false],
  ])('reads streaming for %s as %s', (stored, expected) => {
    expect(isStreaming({ [PREFERENCE_KEYS.pageSize]: stored })).toBe(expected)
  })
})
```

Update the import at the top of the file to:

```tsx
import {
  BLOCK_SIZE, PREFERENCE_KEYS, isStreaming, requestSizeOf, showPreviewOf,
  usePreferences, useSetPreference,
} from './usePreferences'
```

The existing `usePreferences` describe block calls `pageSizeOf(result.current.data!)` — change that
call to `requestSizeOf(result.current.data!)`. Keep everything else in the file as it is.

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/frontend && npm run test -- usePreferences`

Expected: FAIL — `No "requestSizeOf" export is defined on the module`.

- [ ] **Step 3: Write the accessors**

In `src/frontend/src/hooks/usePreferences.ts`, replace `pageSizeOf` with:

```ts
/** How many messages one request asks for. Blocks in streaming mode: the wire has no notion
    of "all" — it is paging, just without a pager. */
export const BLOCK_SIZE = 100

const ALL = 'all'

export function requestSizeOf(preferences: Preferences): number {
  const stored = preferences[PREFERENCE_KEYS.pageSize]
  return stored === ALL ? BLOCK_SIZE : Number(stored)
}

/** The only reader of the raw "all", so a NaN cannot be born anywhere else. */
export function isStreaming(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.pageSize] === ALL
}
```

Leave `showPreviewOf` untouched.

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/frontend && npm run test -- usePreferences`

Expected: PASS.

- [ ] **Step 5: Write the failing combo tests**

In `src/frontend/src/modules/settings/general/GeneralPage.test.tsx`, replace the
`offers the five steps` test with:

```tsx
  it('offers the five steps and All', async () => {
    renderPage()

    const options = Array.from((await screen.findByLabelText('Messages per page')).querySelectorAll('option'))
    expect(options.map(o => o.value)).toEqual(['10', '20', '30', '50', '100', 'all'])
    expect(options.map(o => o.textContent)).toEqual(['10', '20', '30', '50', '100', 'All'])
  })

  it('shows All as the selection when it is stored', async () => {
    renderPage({ 'mail.pageSize': 'all', 'mail.showPreview': 'true' })

    expect(await screen.findByLabelText('Messages per page')).toHaveValue('all')
  })

  it('saves All as the string the backend accepts', async () => {
    renderPage()

    fireEvent.change(await screen.findByLabelText('Messages per page'), { target: { value: 'all' } })

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.pageSize', 'all'))
  })
```

- [ ] **Step 6: Run the test to verify it fails**

Run: `cd src/frontend && npm run test -- GeneralPage`

Expected: FAIL — the options array is `['10','20','30','50','100']`.

- [ ] **Step 7: Rewrite the combo**

In `GeneralPage.tsx`, replace the `PAGE_SIZES` constant:

```tsx
const PAGE_SIZE_OPTIONS = [
  { value: '10', label: '10' },
  { value: '20', label: '20' },
  { value: '30', label: '30' },
  { value: '50', label: '50' },
  { value: '100', label: '100' },
  { value: 'all', label: 'All' },
]

function pageSizeToast(value: string): string {
  return value === 'all'
    ? 'The message list now shows every message'
    : `The message list now shows ${value} per page`
}
```

Change the import to drop `pageSizeOf`:

```tsx
import {
  PREFERENCE_KEYS, showPreviewOf, usePreferences, useSetPreference,
} from '../../../hooks/usePreferences'
```

Replace the `<select>`'s value, handler and options:

```tsx
            <select
              id="page-size"
              value={preferences[PREFERENCE_KEYS.pageSize]}
              disabled={setPreference.isPending}
              onChange={event =>
                save(PREFERENCE_KEYS.pageSize, event.target.value, pageSizeToast(event.target.value))}
            >
              {PAGE_SIZE_OPTIONS.map(option =>
                <option key={option.value} value={option.value}>{option.label}</option>)}
            </select>
```

The select now edits the stored value directly — there is no number to convert, which is the
point: `all` has no numeric form.

- [ ] **Step 8: Update the one remaining caller so the build passes**

In `src/frontend/src/modules/mail/list/MessageList.tsx`, change line 3's import and line 29:

```tsx
import { requestSizeOf, showPreviewOf, usePreferences } from '../../../hooks/usePreferences'
```

```tsx
  const pageSize = preferences ? requestSizeOf(preferences) : 0
```

This is a rename only. `MessageList` is rewired properly in Task 7.

- [ ] **Step 9: Run typecheck, lint and the whole suite**

Run: `cd src/frontend && npm run typecheck && npm run lint && npm run test`

Expected: typecheck clean, lint 0 errors, all tests pass.

- [ ] **Step 10: Commit**

```bash
git add src/frontend/src/hooks/usePreferences.ts \
        src/frontend/src/hooks/usePreferences.test.tsx \
        src/frontend/src/modules/settings/general/GeneralPage.tsx \
        src/frontend/src/modules/settings/general/GeneralPage.test.tsx \
        src/frontend/src/modules/mail/list/MessageList.tsx
git commit -m "Offer All in the page-size combo

Split the accessor: a request size that is always a number, and the mode."
```

---

### Task 3: The pure stream functions

**Files:**
- Create: `src/frontend/src/modules/mail/list/messageStream.ts`
- Test: `src/frontend/src/modules/mail/list/messageStream.test.ts`

**Interfaces:**
- Consumes: `MailFolderPage`, `MailMessageSummary` from `../api/mailTypes`.
- Produces:
  - `dedupeByUid(pages: MailFolderPage[]): MailMessageSummary[]`
  - `nextBlockIndex(lastPage: MailFolderPage, loadedBlocks: number, requestSize: number): number | undefined`
  - `PREFETCH_ROWS = 20`
  - `sentinelIndexOf(messageCount: number): number`

- [ ] **Step 1: Write the failing tests**

Create `src/frontend/src/modules/mail/list/messageStream.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import type { MailFolderPage, MailMessageSummary } from '../api/mailTypes'
import { PREFETCH_ROWS, dedupeByUid, nextBlockIndex, sentinelIndexOf } from './messageStream'

function message(uid: number): MailMessageSummary {
  return {
    uid, subject: `s${uid}`, fromName: '', fromAddress: 'a@b.c', date: '2026-07-21T00:00:00Z',
    seen: false, flagged: false, answered: false, hasAttachments: false, size: 0, preview: '',
  }
}

function page(uids: number[], total = 1000): MailFolderPage {
  return {
    folderPath: 'INBOX', uidValidity: 1, total, page: 0, pageSize: 100,
    messages: uids.map(message),
  }
}

describe('dedupeByUid', () => {
  it('flattens the blocks in order', () => {
    expect(dedupeByUid([page([3, 2]), page([1])]).map(m => m.uid)).toEqual([3, 2, 1])
  })

  // Paging is a numeric offset: one message arriving between two blocks shifts everything by
  // one and the last row of block 1 reappears at the head of block 2.
  it('keeps the first occurrence when a block repeats a uid', () => {
    expect(dedupeByUid([page([3, 2]), page([2, 1])]).map(m => m.uid)).toEqual([3, 2, 1])
  })

  it('answers an empty list for no blocks', () => {
    expect(dedupeByUid([])).toEqual([])
  })
})

describe('nextBlockIndex', () => {
  it('asks for the next block after a full one', () => {
    expect(nextBlockIndex(page([1, 2, 3]), 1, 3)).toBe(1)
  })

  it('stops on a partial block', () => {
    expect(nextBlockIndex(page([1, 2]), 1, 3)).toBeUndefined()
  })

  it('stops on an empty folder', () => {
    expect(nextBlockIndex(page([]), 1, 3)).toBeUndefined()
  })

  // 300 messages in blocks of 100: every block is full, so the stop can only come from the
  // empty block that follows. An implementation written by eye misses this.
  it('asks for one more block when the folder is an exact multiple, then stops', () => {
    expect(nextBlockIndex(page([1, 2, 3]), 3, 3)).toBe(3)
    expect(nextBlockIndex(page([]), 4, 3)).toBeUndefined()
  })

  // total moves when mail arrives; a short block is an observed fact.
  it('ignores a total that disagrees with the blocks', () => {
    expect(nextBlockIndex(page([1, 2], 9999), 1, 3)).toBeUndefined()
  })
})

describe('sentinelIndexOf', () => {
  it('sits PREFETCH_ROWS before the last row', () => {
    expect(sentinelIndexOf(100)).toBe(100 - PREFETCH_ROWS)
  })

  it('sits at the top while the list is shorter than the margin', () => {
    expect(sentinelIndexOf(5)).toBe(0)
    expect(sentinelIndexOf(0)).toBe(0)
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/frontend && npm run test -- messageStream`

Expected: FAIL — `Failed to resolve import "./messageStream"`.

- [ ] **Step 3: Write the implementation**

Create `src/frontend/src/modules/mail/list/messageStream.ts`:

```ts
import type { MailFolderPage, MailMessageSummary } from '../api/mailTypes'

/** How far before the last row the next block starts loading. Rows, not pixels: row height
    changes with the preview setting, so a pixel margin would mean a different number of
    messages depending on a setting that has nothing to do with it. */
export const PREFETCH_ROWS = 20

export function dedupeByUid(pages: MailFolderPage[]): MailMessageSummary[] {
  const seen = new Set<number>()
  const messages: MailMessageSummary[] = []

  for (const page of pages) {
    for (const message of page.messages) {
      if (seen.has(message.uid)) continue
      seen.add(message.uid)
      messages.push(message)
    }
  }

  return messages
}

/** Stops on a short block rather than on `total`: the total moves when mail arrives. */
export function nextBlockIndex(
  lastPage: MailFolderPage, loadedBlocks: number, requestSize: number,
): number | undefined {
  return lastPage.messages.length < requestSize ? undefined : loadedBlocks
}

export function sentinelIndexOf(messageCount: number): number {
  return Math.max(0, messageCount - PREFETCH_ROWS)
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/frontend && npm run test -- messageStream`

Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/list/messageStream.ts \
        src/frontend/src/modules/mail/list/messageStream.test.ts
git commit -m "Add the stream's pure rules

Dedupe by uid, stop on a short block, place the sentinel 20 rows early."
```

---

### Task 4: The infinite query

**Files:**
- Modify: `src/frontend/src/modules/mail/queries.ts:11-21` (keys), `:36-46` (`useMessages`)
- Test: `src/frontend/src/modules/mail/queries.test.tsx`

**Interfaces:**
- Consumes: `nextBlockIndex` from `./list/messageStream`; `api.getMailMessages(folder, page, pageSize, options)`.
- Produces:
  - `mailKeys.messageStream(accountId, folder, requestSize)`
  - `useMessages(folderPath, page, pageSize, enabled = true)` — an `enabled` parameter is added
  - `useMessageStream(folderPath: string | null, requestSize: number, enabled: boolean)` returning
    TanStack's infinite-query result over `MailFolderPage`

- [ ] **Step 1: Write the failing tests**

Append to `src/frontend/src/modules/mail/queries.test.tsx`, following the mocking arrangement the
file already uses for `api`:

```tsx
describe('useMessageStream', () => {
  it('asks for block 0 first', async () => {
    mocks.getMailMessages.mockResolvedValue(pageOf([1, 2], 2))

    const { result } = renderHook(() => useMessageStream('INBOX', 100, true), { wrapper })

    await waitFor(() => expect(result.current.data).toBeDefined())
    expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 0, 100, expect.anything())
  })

  it('issues no request when it is not the active mode', () => {
    renderHook(() => useMessageStream('INBOX', 100, false), { wrapper })

    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  it('fetches the next block by index', async () => {
    mocks.getMailMessages.mockResolvedValue(pageOf([1, 2], 500))

    const { result } = renderHook(() => useMessageStream('INBOX', 2, true), { wrapper })
    await waitFor(() => expect(result.current.hasNextPage).toBe(true))
    result.current.fetchNextPage()

    await waitFor(() =>
      expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 1, 2, expect.anything()))
  })

  it('reports no next block after a short one', async () => {
    mocks.getMailMessages.mockResolvedValue(pageOf([1], 1))

    const { result } = renderHook(() => useMessageStream('INBOX', 2, true), { wrapper })

    await waitFor(() => expect(result.current.data).toBeDefined())
    expect(result.current.hasNextPage).toBe(false)
  })
})
```

Add this helper next to the file's other helpers:

```tsx
function pageOf(uids: number[], total: number) {
  return {
    folderPath: 'INBOX', uidValidity: 1, total, page: 0, pageSize: uids.length,
    messages: uids.map(uid => ({
      uid, subject: '', fromName: '', fromAddress: '', date: '2026-07-21T00:00:00Z',
      seen: true, flagged: false, answered: false, hasAttachments: false, size: 0, preview: '',
    })),
  }
}
```

Import `useMessageStream` alongside the hooks the file already imports from `./queries`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/frontend && npm run test -- queries`

Expected: FAIL — `No "useMessageStream" export is defined on the module`.

- [ ] **Step 3: Add the key and the query**

In `src/frontend/src/modules/mail/queries.ts`, add to the imports:

```ts
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { nextBlockIndex } from './list/messageStream'
```

Add to `mailKeys`, after `messages`:

```ts
  // Its own key: what it caches is not a page but a sequence of pages, and mixing the two
  // shapes under one key is a type error that only shows at runtime.
  messageStream: (accountId: string, folder: string, requestSize: number) =>
    ['mail', accountId, 'messageStream', folder, requestSize] as const,
```

Give `useMessages` an `enabled` parameter — the streaming mode needs it switched off:

```ts
export function useMessages(
  folderPath: string | null, page: number, pageSize: number, enabled = true,
) {
  const accountId = useAccountId()

  return useQuery<MailFolderPage>({
    queryKey: mailKeys.messages(accountId, folderPath ?? '', page, pageSize),
    queryFn: ({ signal }) => api.getMailMessages(folderPath, page, pageSize, { signal }),
    enabled: enabled && folderPath !== null,
    // Keeps the current page on screen while the next one loads, instead of flashing empty.
    placeholderData: (previous) => previous,
  })
}
```

Add the stream below it:

```ts
export function useMessageStream(folderPath: string | null, requestSize: number, enabled: boolean) {
  const accountId = useAccountId()

  return useInfiniteQuery({
    queryKey: mailKeys.messageStream(accountId, folderPath ?? '', requestSize),
    queryFn: ({ pageParam, signal }) =>
      api.getMailMessages(folderPath, pageParam, requestSize, { signal }) as Promise<MailFolderPage>,
    initialPageParam: 0,
    getNextPageParam: (lastPage, allPages) =>
      nextBlockIndex(lastPage, allPages.length, requestSize),
    enabled: enabled && folderPath !== null && requestSize > 0,
    // TanStack refetches *every* loaded block on focus. Forty blocks is forty IMAP
    // connections and forty full folder sorts, so this stays off.
    refetchOnWindowFocus: false,
  })
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/frontend && npm run test -- queries`

Expected: PASS.

- [ ] **Step 5: Typecheck and commit**

Run: `cd src/frontend && npm run typecheck && npm run lint`

Expected: clean.

```bash
git add src/frontend/src/modules/mail/queries.ts src/frontend/src/modules/mail/queries.test.tsx
git commit -m "Add the message stream query

No refetch on focus: forty loaded blocks would be forty IMAP connections."
```

---

### Task 5: The one-shape hook

**Files:**
- Create: `src/frontend/src/modules/mail/list/useMessageList.ts`
- Test: `src/frontend/src/modules/mail/list/useMessageList.test.tsx`

**Interfaces:**
- Consumes: `usePreferences`, `requestSizeOf`, `isStreaming` from `../../../hooks/usePreferences`;
  `useMessages`, `useMessageStream` from `../queries`; `dedupeByUid` from `./messageStream`.
- Produces:

```ts
export interface MessageListState {
  messages: MailMessageSummary[]
  total: number
  isLoading: boolean
  isError: boolean
  paging: { page: number; lastPage: number; onSelect: (page: number) => void } | null
  streaming: {
    hasMore: boolean
    isLoadingMore: boolean
    loadMoreFailed: boolean
    loadMore: () => void
  } | null
}

export function useMessageList(folderPath: string | null): MessageListState
```

- [ ] **Step 1: Write the failing tests**

Create `src/frontend/src/modules/mail/list/useMessageList.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useMessageList } from './useMessageList'

const mocks = vi.hoisted(() => ({ getMailMessages: vi.fn(), getPreferences: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' } }),
}))

let client: QueryClient
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

function pageOf(uids: number[], total: number) {
  return {
    folderPath: 'INBOX', uidValidity: 1, total, page: 0, pageSize: uids.length,
    messages: uids.map(uid => ({
      uid, subject: '', fromName: '', fromAddress: '', date: '2026-07-21T00:00:00Z',
      seen: true, flagged: false, answered: false, hasAttachments: false, size: 0, preview: '',
    })),
  }
}

describe('useMessageList', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  })

  it('pages when a size is chosen', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '10', 'mail.showPreview': 'true' })
    mocks.getMailMessages.mockResolvedValue(pageOf([1, 2], 25))

    const { result } = renderHook(() => useMessageList('INBOX'), { wrapper })

    await waitFor(() => expect(result.current.paging).not.toBeNull())
    expect(result.current.streaming).toBeNull()
    expect(result.current.paging).toMatchObject({ page: 0, lastPage: 2 })
  })

  it('streams on "all", and asks for blocks of 100', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': 'all', 'mail.showPreview': 'true' })
    mocks.getMailMessages.mockResolvedValue(pageOf([1, 2], 2))

    const { result } = renderHook(() => useMessageList('INBOX'), { wrapper })

    await waitFor(() => expect(result.current.streaming).not.toBeNull())
    expect(result.current.paging).toBeNull()
    expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 0, 100, expect.anything())
  })

  // The inactive mode must not fire a second request for the same folder.
  it('runs exactly one of the two queries', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': 'all', 'mail.showPreview': 'true' })
    mocks.getMailMessages.mockResolvedValue(pageOf([1], 1))

    const { result } = renderHook(() => useMessageList('INBOX'), { wrapper })

    await waitFor(() => expect(result.current.messages).toHaveLength(1))
    expect(mocks.getMailMessages).toHaveBeenCalledTimes(1)
  })

  it('waits for the preferences before claiming a mode', () => {
    mocks.getPreferences.mockReturnValue(new Promise(() => {}))

    const { result } = renderHook(() => useMessageList('INBOX'), { wrapper })

    expect(result.current.isLoading).toBe(true)
    expect(result.current.paging).toBeNull()
    expect(result.current.streaming).toBeNull()
    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  it('reports the total from the freshest block', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': 'all', 'mail.showPreview': 'true' })
    mocks.getMailMessages.mockResolvedValue(pageOf([1], 3812))

    const { result } = renderHook(() => useMessageList('INBOX'), { wrapper })

    await waitFor(() => expect(result.current.total).toBe(3812))
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/frontend && npm run test -- useMessageList`

Expected: FAIL — `Failed to resolve import "./useMessageList"`.

- [ ] **Step 3: Write the hook**

Create `src/frontend/src/modules/mail/list/useMessageList.ts`:

```ts
import { useEffect, useMemo, useState } from 'react'
import { isStreaming, requestSizeOf, usePreferences } from '../../../hooks/usePreferences'
import type { MailFolderPage, MailMessageSummary } from '../api/mailTypes'
import { useMessageStream, useMessages } from '../queries'
import { dedupeByUid } from './messageStream'

export interface MessageListState {
  messages: MailMessageSummary[]
  total: number
  isLoading: boolean
  isError: boolean
  paging: { page: number; lastPage: number; onSelect: (page: number) => void } | null
  streaming: {
    hasMore: boolean
    isLoadingMore: boolean
    loadMoreFailed: boolean
    loadMore: () => void
  } | null
}

const WAITING: MessageListState = {
  messages: [], total: 0, isLoading: true, isError: false, paging: null, streaming: null,
}

// A fresh [] on every render would change the memo's dependency every render.
const NO_BLOCKS: MailFolderPage[] = []

/**
 * One shape for both modes, so the list renders a pager or a sentinel without ever learning
 * what "All" means. Both queries are always called — hooks cannot be conditional — with the
 * inactive one disabled, which issues no request.
 */
export function useMessageList(folderPath: string | null): MessageListState {
  const [page, setPage] = useState(0)
  useEffect(() => { setPage(0) }, [folderPath])

  const { data: preferences } = usePreferences()
  const streams = preferences ? isStreaming(preferences) : false
  const requestSize = preferences ? requestSizeOf(preferences) : 0

  const paged = useMessages(folderPath, page, requestSize, Boolean(preferences) && !streams)
  const stream = useMessageStream(folderPath, requestSize, Boolean(preferences) && streams)

  const blocks = stream.data?.pages ?? NO_BLOCKS
  const streamed = useMemo(() => dedupeByUid(blocks), [blocks])

  if (!preferences) return WAITING

  if (streams) {
    return {
      messages: streamed,
      total: blocks.length ? blocks[blocks.length - 1].total : 0,
      isLoading: stream.isLoading,
      isError: stream.isError && blocks.length === 0,
      paging: null,
      streaming: {
        hasMore: stream.hasNextPage,
        isLoadingMore: stream.isFetchingNextPage,
        // A block that failed after others succeeded: the list stays, Retry is offered.
        loadMoreFailed: stream.isError && blocks.length > 0,
        loadMore: () => {
          if (stream.hasNextPage && !stream.isFetchingNextPage) stream.fetchNextPage()
        },
      },
    }
  }

  const total = paged.data?.total ?? 0

  return {
    messages: paged.data?.messages ?? [],
    total,
    isLoading: paged.isLoading,
    isError: paged.isError,
    paging: {
      page,
      lastPage: requestSize > 0 ? Math.max(0, Math.ceil(total / requestSize) - 1) : 0,
      onSelect: setPage,
    },
    streaming: null,
  }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/frontend && npm run test -- useMessageList`

Expected: PASS, 5 tests.

- [ ] **Step 5: Typecheck, lint and commit**

Run: `cd src/frontend && npm run typecheck && npm run lint`

Expected: clean.

```bash
git add src/frontend/src/modules/mail/list/useMessageList.ts \
        src/frontend/src/modules/mail/list/useMessageList.test.tsx
git commit -m "Answer one shape for both list modes

The list gets messages and flags; the mode stops at this hook."
```

---

### Task 6: The sentinel and its test double

**Files:**
- Create: `src/frontend/src/modules/mail/list/LoadMoreSentinel.tsx`
- Create: `src/frontend/src/modules/mail/list/LoadMoreSentinel.test.tsx`
- Modify: `src/frontend/src/test-setup.js`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `<LoadMoreSentinel onReach={() => void} />`; and in `test-setup.js`, a global
  `IntersectionObserver` double exposing `IntersectionObserver.instances` (an array of live
  observers, each with a `trigger(isIntersecting = true)` method), cleared before every test.

- [ ] **Step 1: Add the observer double to the test setup**

Append to `src/frontend/src/test-setup.js`:

```js
// jsdom has no IntersectionObserver. Tests drive this one by hand:
//   IntersectionObserver.instances[0].trigger()
class FakeIntersectionObserver {
  static instances = []

  constructor(callback) {
    this.callback = callback
    FakeIntersectionObserver.instances.push(this)
  }

  observe(element) { this.element = element }
  unobserve() {}
  disconnect() {
    FakeIntersectionObserver.instances =
      FakeIntersectionObserver.instances.filter(observer => observer !== this)
  }

  trigger(isIntersecting = true) {
    this.callback([{ isIntersecting, target: this.element }])
  }
}

window.IntersectionObserver = FakeIntersectionObserver
globalThis.IntersectionObserver = FakeIntersectionObserver

beforeEach(() => { FakeIntersectionObserver.instances = [] })
```

Add `beforeEach` to the vitest import on line 1:

```js
import { beforeEach, vi } from 'vitest'
```

- [ ] **Step 2: Write the failing test**

Create `src/frontend/src/modules/mail/list/LoadMoreSentinel.test.tsx`:

```tsx
import { describe, it, expect, vi } from 'vitest'
import { render } from '@testing-library/react'
import LoadMoreSentinel from './LoadMoreSentinel'

describe('LoadMoreSentinel', () => {
  it('calls back when it comes into view', () => {
    const onReach = vi.fn()
    render(<LoadMoreSentinel onReach={onReach} />)

    IntersectionObserver.instances[0].trigger(true)

    expect(onReach).toHaveBeenCalledTimes(1)
  })

  it('stays quiet while it is out of view', () => {
    const onReach = vi.fn()
    render(<LoadMoreSentinel onReach={onReach} />)

    IntersectionObserver.instances[0].trigger(false)

    expect(onReach).not.toHaveBeenCalled()
  })

  // A re-render hands it a fresh closure. Rebuilding the observer each time would make it
  // fire again on the same intersection, so the callback is read through a ref instead.
  it('keeps one observer across re-renders and calls the latest callback', () => {
    const first = vi.fn()
    const second = vi.fn()
    const { rerender } = render(<LoadMoreSentinel onReach={first} />)

    rerender(<LoadMoreSentinel onReach={second} />)
    expect(IntersectionObserver.instances).toHaveLength(1)

    IntersectionObserver.instances[0].trigger(true)
    expect(first).not.toHaveBeenCalled()
    expect(second).toHaveBeenCalledTimes(1)
  })

  it('disconnects when it unmounts', () => {
    const { unmount } = render(<LoadMoreSentinel onReach={vi.fn()} />)

    unmount()

    expect(IntersectionObserver.instances).toHaveLength(0)
  })
})
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd src/frontend && npm run test -- LoadMoreSentinel`

Expected: FAIL — `Failed to resolve import "./LoadMoreSentinel"`.

- [ ] **Step 4: Write the component**

Create `src/frontend/src/modules/mail/list/LoadMoreSentinel.tsx`:

```tsx
import { useEffect, useRef } from 'react'

interface Props {
  onReach: () => void
}

/**
 * An empty marker that reports when it scrolls into view. It is placed among the rows rather
 * than after the last one, so the next block starts before the reader reaches the end.
 */
export default function LoadMoreSentinel({ onReach }: Props) {
  const ref = useRef<HTMLDivElement>(null)
  const latest = useRef(onReach)
  latest.current = onReach

  useEffect(() => {
    const node = ref.current
    if (!node) return

    const observer = new IntersectionObserver(
      entries => { if (entries.some(entry => entry.isIntersecting)) latest.current() },
      { root: node.closest('.mail-list-scroll') },
    )
    observer.observe(node)

    return () => observer.disconnect()
  }, [])

  return <div ref={ref} className="message-list-sentinel" aria-hidden="true" />
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd src/frontend && npm run test -- LoadMoreSentinel`

Expected: PASS, 4 tests.

- [ ] **Step 6: Run the whole suite — the setup file touches every test**

Run: `cd src/frontend && npm run test`

Expected: PASS, no regression from the new global.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/test-setup.js \
        src/frontend/src/modules/mail/list/LoadMoreSentinel.tsx \
        src/frontend/src/modules/mail/list/LoadMoreSentinel.test.tsx
git commit -m "Add the scroll sentinel

One observer across re-renders: the callback is read through a ref."
```

---

### Task 7: `MessageList` consumes the hook

No behaviour changes in this task. It is the rewiring, gated on its own: paged mode must behave
exactly as before.

**Files:**
- Modify: `src/frontend/src/modules/mail/list/MessageList.tsx`
- Test: `src/frontend/src/modules/mail/list/MessageList.test.tsx`

**Interfaces:**
- Consumes: `useMessageList(folderPath)` returning `MessageListState` (Task 5); `showPreviewOf`,
  `usePreferences`.
- Produces: `MessageList` no longer imports `useMessages` or `requestSizeOf`, and holds no `page`
  state.

- [ ] **Step 1: Rewrite the component's data acquisition**

In `src/frontend/src/modules/mail/list/MessageList.tsx`, replace lines 1–36 with:

```tsx
import { showPreviewOf, usePreferences } from '../../../hooks/usePreferences'
import PaperclipIcon from '../../../icons/PaperclipIcon'
import { formatListDate } from './formatDate'
import Pagination from './Pagination'
import { useMessageList } from './useMessageList'

interface Props {
  folderPath: string | null
  folderName?: string
  selectedUid: number | null
  onSelect: (uid: number) => void
}

/**
 * Three bands: a heading, the rows, and the footer. Only the middle one scrolls, so both the
 * folder you are in and the way off the page you are on stay on screen.
 */
export default function MessageList({ folderPath, folderName, selectedUid, onSelect }: Props) {
  const { messages, isLoading, isError, paging } = useMessageList(folderPath)
  const { data: preferences } = usePreferences()
  const showsPreview = preferences ? showPreviewOf(preferences) : true

  if (!folderPath) return <p className="mail-empty">Select a folder</p>
```

Replace the `rows()` guards (old lines 38–41) with:

```tsx
  function rows() {
    if (isLoading) return <p className="mail-empty">Loading messages…</p>
    if (isError) return <p className="mail-empty">Could not load messages.</p>
    if (messages.length === 0) return <p className="mail-empty">No messages</p>
```

Inside `rows()`, change `data.messages.map(...)` to `messages.map(...)`.

Replace the footer (old lines 79–83) with:

```tsx
      {paging && paging.lastPage > 0 && (
        <div className="mail-list-footer">
          <Pagination page={paging.page} lastPage={paging.lastPage} onSelect={paging.onSelect} />
        </div>
      )}
```

- [ ] **Step 2: Run the existing suite to prove nothing regressed**

Run: `cd src/frontend && npm run test -- MessageList`

Expected: PASS — every existing `MessageList` test, unchanged. If a test needed editing to pass,
the rewiring changed behaviour and must be corrected rather than the test.

No test is added in this task. The existing `MessageList` suite **is** the gate: it was written
against the paged behaviour, and this task must not move it. Adding a test that inspects the
component's source for `useState` would assert an implementation detail, not a behaviour, and pass
for the wrong reasons.

- [ ] **Step 3: Run typecheck, lint and the whole suite**

Run: `cd src/frontend && npm run typecheck && npm run lint && npm run test`

Expected: typecheck clean, lint 0 errors, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/frontend/src/modules/mail/list/MessageList.tsx
git commit -m "Read the list from the one-shape hook

No behaviour change: the paged mode is the same, the wiring is not."
```

---

### Task 8: The streaming interface

**Files:**
- Modify: `src/frontend/src/modules/mail/list/MessageList.tsx`
- Modify: `src/frontend/src/styles/mail.css`
- Test: `src/frontend/src/modules/mail/list/MessageList.test.tsx`

**Interfaces:**
- Consumes: `MessageListState.streaming` (Task 5), `sentinelIndexOf` (Task 3),
  `<LoadMoreSentinel onReach>` (Task 6).
- Produces: the finished feature.

- [ ] **Step 1: Write the failing tests**

Append to `src/frontend/src/modules/mail/list/MessageList.test.tsx`. Mock `./useMessageList` so the
component is tested against the shape it consumes rather than against the network:

```tsx
vi.mock('./useMessageList', () => ({ useMessageList: mocks.useMessageList }))

function streamingState(overrides = {}) {
  return {
    messages: Array.from({ length: 100 }, (_, i) => ({
      uid: i + 1, subject: `Subject ${i + 1}`, fromName: 'A', fromAddress: 'a@b.c',
      date: '2026-07-21T00:00:00Z', seen: true, flagged: false, answered: false,
      hasAttachments: false, size: 0, preview: '',
    })),
    total: 3812,
    isLoading: false,
    isError: false,
    paging: null,
    streaming: {
      hasMore: true, isLoadingMore: false, loadMoreFailed: false, loadMore: vi.fn(), ...overrides,
    },
  }
}

describe('MessageList streaming', () => {
  it('shows the counter instead of a pager', () => {
    mocks.useMessageList.mockReturnValue(streamingState())
    renderList()

    expect(screen.getByText('100 of 3,812')).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Pages' })).not.toBeInTheDocument()
  })

  it('places the sentinel 20 rows before the last', () => {
    mocks.useMessageList.mockReturnValue(streamingState())
    const { container } = renderList()

    const rows = Array.from(container.querySelectorAll('.message-list > li'))
    const carrying = rows.findIndex(row => row.querySelector('.message-list-sentinel'))
    expect(carrying).toBe(80)
  })

  it('asks for the next block when the sentinel comes into view', () => {
    const loadMore = vi.fn()
    mocks.useMessageList.mockReturnValue(streamingState({ loadMore }))
    renderList()

    IntersectionObserver.instances[0].trigger(true)

    expect(loadMore).toHaveBeenCalledTimes(1)
  })

  it('says a block is on its way', () => {
    mocks.useMessageList.mockReturnValue(streamingState({ isLoadingMore: true }))
    renderList()

    expect(screen.getByText('Loading more…')).toBeInTheDocument()
  })

  // The point of the whole error path: three thousand valid rows must not be erased because
  // the three-thousand-and-first did not arrive.
  it('keeps the loaded rows when a block fails, and offers Retry', () => {
    const loadMore = vi.fn()
    mocks.useMessageList.mockReturnValue(
      streamingState({ loadMoreFailed: true, hasMore: true, loadMore }))
    renderList()

    expect(screen.getByText('Subject 1')).toBeInTheDocument()
    expect(screen.queryByText('Could not load messages.')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(loadMore).toHaveBeenCalledTimes(1)
  })

  it('drops the sentinel and the counter on an empty folder', () => {
    mocks.useMessageList.mockReturnValue({
      messages: [], total: 0, isLoading: false, isError: false, paging: null,
      streaming: { hasMore: false, isLoadingMore: false, loadMoreFailed: false, loadMore: vi.fn() },
    })
    const { container } = renderList()

    expect(screen.getByText('No messages')).toBeInTheDocument()
    expect(container.querySelector('.message-list-sentinel')).toBeNull()
    expect(container.querySelector('.mail-list-count')).toBeNull()
  })

  it('returns to the top when the folder changes', () => {
    mocks.useMessageList.mockReturnValue(streamingState())
    const { container, rerender } = renderList()

    const band = container.querySelector('.mail-list-scroll') as HTMLDivElement
    band.scrollTop = 900
    rerender(<MessageList folderPath="Archive" selectedUid={null} onSelect={vi.fn()} />)

    expect(band.scrollTop).toBe(0)
  })
})
```

Add `useMessageList: vi.fn()` to the file's `vi.hoisted` mocks object, and make the existing
paged-mode tests set `mocks.useMessageList.mockReturnValue(...)` with a `paging` state rather than
relying on the network mocks. Adapt `renderList()` to the file's existing helper.

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/frontend && npm run test -- MessageList`

Expected: FAIL — `Unable to find an element with the text: 100 of 3,812`.

- [ ] **Step 3: Render the streaming interface**

In `MessageList.tsx`, take `streaming` and `total` from the hook and add the scroll band ref:

```tsx
import { useEffect, useRef } from 'react'
import LoadMoreSentinel from './LoadMoreSentinel'
import { sentinelIndexOf } from './messageStream'
```

```tsx
const COUNT = new Intl.NumberFormat('en-US')
```

```tsx
  const { messages, total, isLoading, isError, paging, streaming } = useMessageList(folderPath)
  const scrollRef = useRef<HTMLDivElement>(null)

  // The page index resets on its own; the DOM scroll position does not, and would drop the
  // reader into the middle of a folder whose blocks are not loaded.
  useEffect(() => { if (scrollRef.current) scrollRef.current.scrollTop = 0 }, [folderPath])
```

Inside `rows()`, compute the sentinel row and render it inside that row's `<li>`:

```tsx
    const sentinelRow = streaming?.hasMore ? sentinelIndexOf(messages.length) : -1

    return (
      <ul className="message-list">
        {messages.map((message, index) => {
```

```tsx
          return (
            <li key={message.uid}>
              {streaming && index === sentinelRow && <LoadMoreSentinel onReach={streaming.loadMore} />}
              <button type="button" className={classes.join(' ')} onClick={() => onSelect(message.uid)}>
```

After the `</ul>`, still inside `rows()`'s returned fragment, add the two block states — wrap the
list and these in a `<>…</>`:

```tsx
        {streaming?.isLoadingMore && <p className="mail-block-state">Loading more…</p>}
        {streaming?.loadMoreFailed && (
          <p className="mail-block-state">
            Could not load more.{' '}
            <button type="button" className="mail-retry" onClick={streaming.loadMore}>Retry</button>
          </p>
        )}
```

Give the scrolling band its ref, and add the counter to the footer:

```tsx
      <div className="mail-list-scroll" ref={scrollRef}>{rows()}</div>

      {paging && paging.lastPage > 0 && (
        <div className="mail-list-footer">
          <Pagination page={paging.page} lastPage={paging.lastPage} onSelect={paging.onSelect} />
        </div>
      )}

      {/* Loaded / total. Removing this block removes the counter and nothing else. */}
      {streaming && total > 0 && (
        <div className="mail-list-footer">
          <span className="mail-list-count">{COUNT.format(messages.length)} of {COUNT.format(total)}</span>
        </div>
      )}
```

- [ ] **Step 4: Add the styles**

Append to `src/frontend/src/styles/mail.css`, after the `.mail-list-footer` rule:

```css
.mail-list-count {
  display: block;
  padding: 8px 12px;
  font-size: 12px;
  color: var(--text-muted);
  text-align: center;
}

.mail-block-state {
  padding: 10px 12px;
  font-size: 12px;
  color: var(--text-muted);
  text-align: center;
}

.mail-retry {
  border: none;
  background: none;
  padding: 0;
  font: inherit;
  color: var(--action-primary);
  cursor: pointer;
  text-decoration: underline;
}
```

Check that `--text-muted` exists in `src/frontend/src/styles/tokens.css`; if it does not, use the
token the surrounding mail rules already use for secondary text and keep the literal colour count
at zero.

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd src/frontend && npm run test -- MessageList`

Expected: PASS.

- [ ] **Step 6: Run typecheck, lint and the whole suite**

Run: `cd src/frontend && npm run typecheck && npm run lint && npm run test`

Expected: typecheck clean, lint 0 errors, every test passing.

- [ ] **Step 7: Verify the scroll in a real browser**

The tests drive a fake observer, so they prove `loadMore` behaves — not that scrolling calls it.

```bash
cd src/frontend && npm run build && npm run preview
```

Open the preview, set Messages per page to `All` in Settings → General, open a folder with more
than 100 messages, and scroll. Confirm: a second block arrives **before** the last row is reached,
the counter climbs, and the list does not jump. Then throttle the network in devtools and confirm
the "Loading more…" row appears rather than a frozen list.

- [ ] **Step 8: Commit**

```bash
git add src/frontend/src/modules/mail/list/MessageList.tsx \
        src/frontend/src/modules/mail/list/MessageList.test.tsx \
        src/frontend/src/styles/mail.css
git commit -m "Load the next block before the reader reaches the end

A failed block keeps the loaded rows and offers Retry."
```

---

### Task 9: Document the slice

**Files:**
- Modify: `src/frontend/CLAUDE.md`

**Interfaces:**
- Consumes: the finished feature.
- Produces: nothing code-facing.

- [ ] **Step 1: Update the frontend guide**

In `src/frontend/CLAUDE.md`, extend the mail-module file list and the Preferences paragraph with:

- `list/useMessageList.ts` — one shape for both modes; `MessageList` never learns what "All"
  means. Both queries are always called, the inactive one disabled.
- `list/messageStream.ts` — `dedupeByUid` (paging is a numeric offset, so one arriving message
  makes a row reappear in the next block), `nextBlockIndex` (stops on a short block, never on
  `total`, which moves), `sentinelIndexOf`.
- `list/LoadMoreSentinel.tsx` — placed `PREFETCH_ROWS = 20` rows before the last, in rows and
  never in pixels: row height changes with the preview setting. The margin must stay well under
  `BLOCK_SIZE`, or each arriving block would trigger the next and the folder would load itself.
- The stream query sets `refetchOnWindowFocus: false` — TanStack refetches every loaded block,
  so forty blocks would be forty IMAP connections and forty full folder sorts.
- A failed block keeps the loaded rows and offers Retry; only a failed **first** block shows
  "Could not load messages."

- [ ] **Step 2: Commit**

```bash
git add src/frontend/CLAUDE.md
git commit -m "Document the streaming message list

Records why the margin is in rows and why focus refetching is off."
```
