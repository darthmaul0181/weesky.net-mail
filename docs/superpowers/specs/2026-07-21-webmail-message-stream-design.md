# Webmail — "All" messages per page, loaded in blocks

**Goal:** add `All` to the messages-per-page setting. The list then has no pager: it loads a
first block and fetches the next as the reader scrolls to the end.

**Scope:** this slice only. Periodic refresh of the list — polling for new mail the way Rainloop
does — is a separate design, deliberately not folded in here. See *Out of scope* at the end,
which records what was decided about it so the next slice does not re-litigate it.

---

## Why blocks of 100

Each request opens its own IMAP connection — connection pooling was considered and rejected —
authenticates, and asks the server to `SORT` the **whole** folder before the requested window is
cut out of it. That cost is identical whether the block holds 30 messages or 200; only the
header `FETCH` scales with the block.

Scrolling to the bottom of a 17,000-message folder in blocks of 30 therefore means ~560
connections and ~560 full sorts. In blocks of 100 it is three times fewer, and 100 rows are
already several screens — the next block has time to arrive before the reader reaches the void.

`BLOCK_SIZE = 100` is an internal constant: not stored, not shown, not configurable. It happens
to equal the largest value in the settings combo; that is a coincidence, not a coupling, and it
is declared separately so it can change without touching the combo.

---

## The backend learns nothing

One line changes, in the preference registry:

```csharp
new(MailPageSize, "30", ["10", "20", "30", "50", "100", "all"]),
```

`ImapSession.ListMessagesAsync` is untouched. In streaming mode the client asks for
`page=0, pageSize=100`, then `page=1`, then `page=2` — ordinary pages. **The server never learns
that "All" exists**, which is the point: the word means something to the interface only, and the
offset window it consumes is the one the server already serves.

---

## The silent failure this must not create

`pageSizeOf` currently returns `Number(preferences['mail.pageSize'])`. On `"all"` that is `NaN`,
and `NaN` throws nothing: it propagates through `Math.ceil(total / NaN)`, into the request URL,
and surfaces as an empty list or a 400 that names nothing. This project has already been bitten
by a value that was wrong without being an error (`bgcolor` given `rgb()`).

The accessor is therefore split in two, because it was answering two questions that had never
needed separating:

```ts
export const BLOCK_SIZE = 100

/** How many messages one request asks for. In "all" mode this is the block size: the wire
    has no notion of "all" — it is paging, just without a pager. */
export function requestSizeOf(preferences: Preferences): number

/** Whether the list pages or streams. The only reader of the raw "all". */
export function isStreaming(preferences: Preferences): boolean
```

`requestSizeOf` always returns a usable number. `isStreaming` is the single place in the codebase
that compares against the string `"all"`, so a `NaN` can no longer be born anywhere.

In `GeneralPage`, the combo goes from a list of numbers to a list of value/label pairs, to carry
`all` → `All`. Labels stay English, like the rest of the interface.

---

## Frontend decomposition

The risk is not the scrolling. It is `MessageList` becoming a component with two brains: two data
sources, two footers, two empty states, two error paths. It is 86 lines today; written naively it
would be ~170, half of them branches.

### One shape, both modes

A new hook, `list/useMessageList.ts`, always answers the same thing:

```ts
interface MessageListState {
  messages: MailMessageSummary[]
  total: number
  isLoading: boolean          // first load, nothing to show yet
  isError: boolean            // first load failed
  paging:    { page: number; lastPage: number; onSelect: (page: number) => void } | null
  streaming: { hasMore: boolean; isLoadingMore: boolean; loadMoreFailed: boolean;
               loadMore: () => void } | null
}
```

`total` is read from the **most recently loaded** page rather than the first, so the counter
reflects the folder as the server last described it. Exactly one of `paging` / `streaming` is
non-null. That is the discriminant: `MessageList` renders
a pager when it gets `paging`, a sentinel when it gets `streaming`, and **does not know what "All"
means**. It no longer knows about TanStack Query either — it receives messages and flags.

The hook calls both queries with only one enabled. React hooks cannot be conditional; the inactive
one is `enabled: false`, which is the idiomatic form and issues no request. The `page` state moves
out of `MessageList` into this hook: it belongs with the data, not with the rendering.

### The stream, in `queries.ts`

A `useInfiniteQuery` under its own key, `mailKeys.messageStream(accountId, folder, requestSize)`.
Distinct from `mailKeys.messages` because what it caches is not a page but a sequence of pages;
mixing the two shapes under one key is a type error that only shows at runtime.

Two settings on that query are not negotiable:

**`refetchOnWindowFocus: false`.** TanStack treats loaded blocks as one unit and refetches *all*
of them, in order. Forty blocks loaded means forty IMAP connections and forty full sorts on a
return to the tab — turning today's harmless behaviour into the most expensive operation in the
application. It is TanStack's default, so **an omission is enough to reintroduce the problem**; it
carries its comment.

**Stop on `lastPage.messages.length < requestSize`, not on `total`.** The total is a server value
that moves when mail arrives; an incomplete block is an observed fact.

### Two pure functions

In `list/messageStream.ts`, testable without React or network — the pattern already in place with
`pageList.ts` and `folderNodes.ts`:

- **`dedupeByUid(pages)`** — flattens and keeps the first occurrence of each `uid`. Paging is a
  numeric offset, so a single message arriving between block 1 and block 2 shifts everything by
  one and makes the last row of block 1 reappear at the head of block 2. This is not an exotic
  case; it is how a live inbox behaves.
- **`nextBlockIndex(lastPage, loadedBlocks, requestSize)`** — the stop rule above, lifted out of
  the hook so its edges can be exercised.

### The sentinel, and why it is not at the end

`list/LoadMoreSentinel.tsx`: an empty `<div>` watched by an `IntersectionObserver` whose root is
the scrolling band, calling `loadMore` when it comes into view. It is its own file because `jsdom`
does not implement `IntersectionObserver` — a stub is needed, and one file should depend on it
rather than the whole list component.

It sits **`PREFETCH_ROWS = 20` rows before the last one**, not after it. Waiting for the reader to
reach the end guarantees they see the wait; starting a block early makes it invisible at reading
speed.

Twenty *rows*, not a pixel margin on the observer. A pixel threshold is not stable under the
preview setting: a row is ~72px with the preview on and ~48px without, so the same margin would
fire 20 messages from the end in one case and 30 in the other, for no reason anyone could name.

**The margin must stay well under the block size.** Near 80 of 100, each arriving block would drop
its new sentinel straight into view, triggering the next, and the whole folder would load itself
with nobody asking — exactly what block loading exists to prevent. At 20 of 100 a block delivers
100 rows and consumes 20 of them only after real scrolling.

This does not abolish the wait: 20 rows is ~1,400px, a fraction of a second on a flicked wheel,
while the request costs a connection, an authentication and a full folder sort. Loading is
invisible at reading speed and still reachable by a violent scroll — which is why the "Loading
more…" row stays. It becomes rare, not useless.

`PREFETCH_ROWS` is internal, like `BLOCK_SIZE`: not stored, not shown, not configurable.

### Files

| | |
|---|---|
| Modify | `src/snoopy.microservice/Models/UserPreferences.cs` (one line) |
| Modify | `src/frontend/src/hooks/usePreferences.ts` (accessors) |
| Modify | `src/frontend/src/modules/settings/general/GeneralPage.tsx` (combo options) |
| Modify | `src/frontend/src/modules/mail/queries.ts` (stream query + key) |
| Modify | `src/frontend/src/modules/mail/list/MessageList.tsx` (bands, no data logic) |
| Create | `src/frontend/src/modules/mail/list/messageStream.ts` |
| Create | `src/frontend/src/modules/mail/list/useMessageList.ts` |
| Create | `src/frontend/src/modules/mail/list/LoadMoreSentinel.tsx` |

---

## States, errors, edges

**Three loading states, not one.** First load keeps what exists — "Loading messages…" instead of
rows, since there is nothing to show. A subsequent block is different: the list is already there
and must not be replaced, so a quiet "Loading more…" row sits under the last one, exactly where
the new rows will appear.

**No end-of-list marker.** The presence or absence of that row already carries "loading" versus
"done". A list that stops *is* the signal. "You have reached the end" in a mailbox is noise in a
column where every row is supposed to be a message.

**A failed block does not destroy what is loaded.** The reflex would be to surface `isError` and
render "Could not load messages." — erasing three thousand valid rows because the three-thousand-
and-first did not arrive. Instead the loading row becomes a failure row with a **Retry** button,
and the list stays. A refused IMAP connection is a transient incident; it must not cost the whole
scroll.

**The counter** occupies the footer band: `200 of 3,812`. Formatted with an explicit `en-US`
locale rather than the machine's — the thousands separator otherwise depends on the environment,
and a test expecting `3,812` would pass locally and fail on the runner. When loaded and total
meet, the counter says so, which is what makes the end marker unnecessary. **This block is
isolated in the footer band**: removing it — should the "no footer at all" variant be preferred
later — touches neither the list nor the loading.

**Scroll returns to the top when the folder changes.** Today the page index resets, which is
invisible on a short list. In streaming mode the DOM scroll position survives, dropping the reader
into the middle of a folder on a block that is not loaded. The band resets its `scrollTop`
explicitly on a change of folder path.

**Changing the setting while reading** is already handled: `useSetPreference` invalidates the whole
`['mail']` cache, so the list restarts from a single block. That is correct — going from All to 30
with four thousand rows on screen cannot mean anything else.

**The open message is independent of the list.** It is loaded by its own query from the `uid` in the
URL, so changing the setting or reloading the page does not close it, even when its row is not in
any loaded block.

An empty folder shows neither sentinel nor counter: "No messages", as today.

---

## Tests

**Pure** — `messageStream.test.ts`. `dedupeByUid`: a `uid` in two blocks appears once, first
occurrence wins, order preserved. `nextBlockIndex`: full block → next index; partial block →
`undefined`; empty folder → `undefined`; **folder size an exact multiple of the block** → next
index, then the empty block that follows ends the stream. That last case is the one a by-eye
implementation misses, and it is entirely ordinary: 300 messages in blocks of 100.

**Accessors** — `requestSizeOf('all')` is 100; `isStreaming` separates the six values; and an
explicit assertion that no accessor returns `NaN` on `"all"`. Without it, the silent failure
identified above would only report itself in production, as an empty list.

**`useMessageList`** — paged mode gives non-null `paging` and null `streaming`, streaming mode the
inverse, the inactive query issues no request, and the deduplication is applied to what it exposes.

**`MessageList`** — pager in one mode, counter in the other; the "Loading more…" row while a block
is in flight; **the sentinel sits 20 rows before the last**, since a value with no test drifts on
the first refactor and nothing signals it; and the one that matters: **a failed block leaves the loaded rows on screen** and
offers Retry. That requirement is easy to break later with a well-meant simplification that
surfaces `isError`.

**Backend** — the registry accepts `all` and still refuses an invented value; `PUT` answers 204 on
`all`.

**Infrastructure** — a fake `IntersectionObserver` in `test-setup.js`, driven by the tests.

**A limit stated plainly:** these tests do not prove that scrolling triggers anything. They prove
`loadMore` does the right thing when called. The link between "the reader reaches the bottom" and
that call rests on a fake observer, so on an assumption. It is verified by rendering headless in
Edge, as the display bugs were.

---

## Out of scope: periodic refresh

Recorded here so the next slice starts from the conclusions rather than the questions.

Paging by offset and periodic refresh sit badly together: a page is a **numeric window**, not a
stable set. Page 5 is not "those fifty messages", it is "messages 401–500 in the current order".
Three messages arrive, everything shifts, and page 5 now shows three messages already seen on page
4 — content changing under the eyes with nobody having clicked.

On **page 1** that same shift is exactly the wanted behaviour. Hence the asymmetry that resolves
it: page 1 and the rest are not asking the same question. Someone on page 1 is watching for mail;
someone on page 5 is consulting history, and for them a self-reordering list is a defect.

Rainloop does not reload the list every N seconds. It asks a **cheap** question — how many
messages, what is the next uid — and only reloads when the answer moved. The distinction matters
here: reloading a list costs a connection, an authentication, a full folder sort and a header
fetch; a `STATUS` costs the connection and nothing else. `UIDNEXT` is the signal, since it rises
strictly on every arrival, and the same answer carries the unseen count the folder tree needs —
one poll, two consumers.

The resulting rule is the same in both modes, which is the sign it is the right one: **reload
automatically when the reader is at the top of the list, otherwise offer it.** Page 1 in paged
mode, or the top of the list in streaming mode: the first block reloads silently. Anywhere else: a
quiet "3 new messages" banner that reloads on click, nothing moving until then. In streaming mode
that click collapses the list back to a single block, which is honest — everything below just
shifted.

**What this slice owes that one:** list state is produced by a single hook (`useMessageList`), so
the poll and the reload attach there without redrawing the component. No further provision is made
— `dedupeByUid` happens to be exactly what the shifted-window reload will need, but it is required
by this slice on its own merits.
