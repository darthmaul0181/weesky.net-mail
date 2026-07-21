# Webmail — periodic refresh of the mail view

**Goal:** mail that arrives, is deleted, or is read elsewhere shows up in the open webmail within
a minute, without the user doing anything.

**The prior art decision that shaped this design:** Rainloop and Outlook Web were both tested by
hand. Neither compensates scroll — when new mail lands, the visible rows shift down by one, even
mid-scroll. Users of webmail expect that behaviour, so this design does not fight it: **new mail
enters the list at every poll tick, wherever the reader is.** No banner, no "only when at the
top" rule, no scroll anchoring. The reading pane is what carries the stability that matters: it
is fed by its own query on the `uid` in the URL, so the text being read never moves a pixel when
the list shifts.

**Scope:** polling, list refresh, badge freshness. Out of scope, recorded at the end: sound
notification, tab-title count, background-tab polling.

---

## Why polling, and why this poll

IMAP has a push mechanism, `IDLE`, and it is out of reach on purpose: it needs a standing
connection per user, exactly what was rejected when connection pooling was rejected. Polling is
the right choice here, not a fallback.

The poll must be cheap because it runs every minute for every open tab. Reloading a message list
costs a connection, an authentication, a full folder `SORT` and a header fetch. Asking what
changed costs a connection and one command: `ListFoldersAsync` already issues a single `LIST`
with `STATUS (MESSAGES UNSEEN UIDVALIDITY)` across every folder. **`GET /Mail/Folders` already is
the cheap poll** — it only lacks `UIDNEXT`.

One poll, two consumers: the folder tree's badges and the list-refresh decision read the same
response.

## Backend: one line, one field

`StatusItems.UidNext` joins the `STATUS` items in `ImapSession.ListFoldersAsync` — a standard
RFC 3501 item, no capability gate needed (unlike `MailboxId`, which stays gated on `OBJECTID`).
`MailFolderNode` gains `UidNext`. No new endpoint, no other backend change.

## Frontend: the poll is a query option, not a timer

`useFolders` gains `refetchInterval: POLL_INTERVAL` (60,000 ms, an internal constant like
`BLOCK_SIZE` and `PREFETCH_ROWS` — never stored, shown, or configurable). TanStack owns the
scheduling. Two behaviours come free:

- **Every unread badge in the tree updates on every tick**, all folders at once — the tree
  already renders from this query. Mail read on a phone drops the badge within the minute.
- The interval pauses while the tab is unfocused (TanStack's default), and the app-wide
  `refetchOnWindowFocus: true` fires an immediate catch-up tick on return. No background
  polling until the sound notification needs it.

### The refresh decision

A pure function compares two snapshots of the **displayed** folder:

```ts
// list/folderDelta.ts
export type FolderSnapshot = { uidNext: number | null; total: number | null;
                               unread: number | null; uidValidity: number }
export function folderChanged(previous: FolderSnapshot, next: FolderSnapshot): boolean
export function uidValidityBroke(previous: FolderSnapshot, next: FolderSnapshot): boolean
```

`folderChanged` is true when `uidNext`, `total` **or** `unread` moved. Not `uidNext` alone:

- a **deletion** made from another client does not change `uidNext` but changes `total`;
- a **read/unread flip** elsewhere changes only `unread` — and the unread dots on the visible
  rows must follow, which they do because the refresh refetches the rows' summaries.

A watcher hook (`useListRefresh`, living beside `useMessageList`) subscribes to the folders
query, snapshots the displayed folder, and on change refreshes the list:

- **Paged mode:** invalidate `mailKeys.messages` for the folder. TanStack refetches the active
  page — one request. Whatever the current page shows after the shift is what it shows; that is
  the tested Rainloop/Outlook behaviour.
- **Streaming mode: never `invalidateQueries`.** TanStack v5 refetches *every* page of an
  infinite query on invalidation — forty loaded blocks would be forty IMAP connections and forty
  full folder sorts, the exact catastrophe `refetchOnWindowFocus: false` exists to prevent.
  Instead: fetch block 0 alone via `api.getMailMessages(folder, 0, BLOCK_SIZE)` and replace
  `pages[0]` in the cache with `setQueryData`. The later blocks stay, now offset by the number
  of arrivals; `dedupeByUid` swallows the boundary duplicates — the exact case it exists for,
  and the one the final review made it prove on non-adjacent blocks.
- **`uidValidityBroke`:** the folder was rebuilt server-side; every cached UID is a lie. Drop
  everything for the folder — full invalidation of its messages, stream and open message. This
  is already the documented doctrine on `MailFolderPage.uidValidity` in `mailTypes.ts`.

### What the user sees

At the top of the list: new mail appears, rows below slide down. Mid-scroll: the same — visible
rows shift by the height of the insertions, as Rainloop and Outlook Web do. The selected
message stays selected (selection is by `uid`, not by position) and the reading pane never
moves. In streaming mode the loaded tail remains loaded; nobody loses four thousand scrolled
rows to two arriving messages.

## Failures are silent

A failed poll tick does nothing: no toast, no error state, the list keeps its content. At one
tick a minute, a transient failure corrects itself on the next tick, and a toast per minute
through a network blip would be unbearable. The 401 path is untouched — it already signs out
through the global handler.

## Tests

- **`folderDelta.test.ts`** (pure): each field moving alone trips `folderChanged`; nothing moving
  trips nothing; `uidValidity` moving trips `uidValidityBroke` and not the ordinary refresh;
  null fields (non-selectable folder) never trip anything.
- **`useListRefresh`** with fake timers: a tick with a changed `uidNext` refreshes the displayed
  folder's list and no other; a tick with nothing changed issues no list request; a failed tick
  is silent.
- **The mutation lock:** a test that fails if streaming refresh ever goes through
  `invalidateQueries` on the stream key — proven by mutation, the same way the
  `refetchOnWindowFocus` lock was. This is the costliest regression the feature can have.
- **Backend:** `ListFoldersAsync` asks for `UidNext` and maps it; the node carries it.

## Out of scope

- **Sound notification** (wanted eventually): the trigger is the `uidNext` delta on the inbox —
  this poll already computes it; the future slice only adds the audible part and, probably,
  `refetchIntervalInBackground` so an unfocused tab still notices.
- **Tab-title unread count**: same trigger, same slice as the sound, most likely.
- **Scroll compensation**: deliberately rejected after testing Rainloop and Outlook Web — see
  the top of this document. Do not reintroduce it as a "fix".
